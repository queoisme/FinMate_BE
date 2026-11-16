"""Bảng nhớ món riêng theo người dùng (``user_item_memory``, ``app.bill.item_memory``), trên Postgres thật."""

import uuid

import pytest_asyncio
from sqlalchemy import text

from app.bill import item_memory
from app.core.anonymizer import sha256_hex

USER = sha256_hex("user-1")
OTHER = sha256_hex("user-2")


@pytest_asyncio.fixture(autouse=True)
async def empty_memory(engine):
    # clean_tables (conftest dùng chung) chưa biết bảng này: tự dọn để mỗi test bắt đầu từ bảng rỗng.
    async with engine.begin() as connection:
        await connection.execute(text("TRUNCATE user_item_memory"))


async def stored(session) -> dict[str, tuple[uuid.UUID, str]]:
    rows = (await session.execute(text("SELECT item_key, category_id, source FROM user_item_memory"))).all()
    return {key: (category, source) for key, category, source in rows}


async def test_record_then_lookup_returns_the_category_id(session):
    category = uuid.uuid4()

    assert await item_memory.record(session, USER, [("Cà phê sữa", category, "accepted")]) == 1

    assert await item_memory.lookup(session, USER, ["ca phe sua"]) == {"ca phe sua": category}


async def test_lookup_of_unknown_keys_or_no_keys_is_empty(session):
    assert await item_memory.lookup(session, USER, ["ca phe sua"]) == {}
    assert await item_memory.lookup(session, USER, []) == {}


async def test_corrected_overwrites_accepted(session):
    old, new = uuid.uuid4(), uuid.uuid4()
    await item_memory.record(session, USER, [("Cà phê sữa", old, "accepted")])

    await item_memory.record(session, USER, [("Cà phê sữa", new, "corrected")])

    assert await stored(session) == {"ca phe sua": (new, "corrected")}


async def test_accepted_does_not_overwrite_corrected(session):
    mine, ai = uuid.uuid4(), uuid.uuid4()
    await item_memory.record(session, USER, [("Cà phê sữa", mine, "corrected")])

    written = await item_memory.record(session, USER, [("Cà phê sữa", ai, "accepted")])

    assert written == 0
    assert await stored(session) == {"ca phe sua": (mine, "corrected")}


async def test_accepted_overwrites_accepted(session):
    old, new = uuid.uuid4(), uuid.uuid4()
    await item_memory.record(session, USER, [("Cà phê sữa", old, "accepted")])

    await item_memory.record(session, USER, [("Cà phê sữa", new, "accepted")])

    assert await stored(session) == {"ca phe sua": (new, "accepted")}


async def test_same_key_twice_in_one_batch_applies_in_order(session):
    a, b = uuid.uuid4(), uuid.uuid4()

    await item_memory.record(session, USER, [("Trà đào", a, "corrected"), ("Trà đào", b, "accepted")])

    assert await stored(session) == {"tra dao": (a, "corrected")}


async def test_unusable_names_are_skipped(session):
    category = uuid.uuid4()

    written = await item_memory.record(session, USER, [
        ("SP002993", category, "corrected"),             # giống mã hàng
        ("ab", category, "corrected"),                   # quá ngắn
        ("Bột giặt 0912345678", category, "corrected"),  # còn chuỗi số dài
    ])

    assert written == 0 and await stored(session) == {}


async def test_cap_keeps_most_recent_and_drops_oldest_accepted_first(session, monkeypatch):
    monkeypatch.setattr(item_memory, "MAX_ROWS_PER_USER", 3)
    ids = {name: uuid.uuid4() for name in ("Trà đào", "Cà phê", "Bánh mì", "Nước cam", "Sinh tố")}
    # mỗi lần gọi là một giao dịch riêng nên updated_at tăng dần theo thứ tự này
    for name, kind in (("Trà đào", "corrected"), ("Cà phê", "accepted"), ("Bánh mì", "accepted"), ("Nước cam", "corrected"), ("Sinh tố", "accepted")):
        await item_memory.record(session, USER, [(name, ids[name], kind)])

    assert set(await stored(session)) == {"tra dao", "nuoc cam", "sinh to"}  # cả hai corrected còn, accepted cũ nhất (cà phê, bánh mì) bị xoá


async def test_cap_drops_oldest_corrected_only_when_nothing_else_is_left(session, monkeypatch):
    monkeypatch.setattr(item_memory, "MAX_ROWS_PER_USER", 2)
    for name in ("Trà đào", "Cà phê", "Bánh mì"):
        await item_memory.record(session, USER, [(name, uuid.uuid4(), "corrected")])

    assert set(await stored(session)) == {"ca phe", "banh mi"}


async def test_cap_is_per_user(session, monkeypatch):
    monkeypatch.setattr(item_memory, "MAX_ROWS_PER_USER", 1)
    await item_memory.record(session, OTHER, [("Trà đào", uuid.uuid4(), "accepted")])

    await item_memory.record(session, USER, [("Cà phê", uuid.uuid4(), "accepted")])

    assert await item_memory.lookup(session, OTHER, ["tra dao"]) != {}


async def test_lookup_and_delete_are_scoped_to_the_user_hash(session):
    mine, theirs = uuid.uuid4(), uuid.uuid4()
    await item_memory.record(session, USER, [("Cà phê sữa", mine, "corrected")])
    await item_memory.record(session, OTHER, [("Cà phê sữa", theirs, "corrected")])

    assert await item_memory.lookup(session, USER, ["ca phe sua"]) == {"ca phe sua": mine}
    assert await item_memory.lookup(session, OTHER, ["ca phe sua"]) == {"ca phe sua": theirs}

    assert await item_memory.delete_by_user_hash(session, USER) == 1

    assert await item_memory.lookup(session, USER, ["ca phe sua"]) == {}
    assert await item_memory.lookup(session, OTHER, ["ca phe sua"]) == {"ca phe sua": theirs}
