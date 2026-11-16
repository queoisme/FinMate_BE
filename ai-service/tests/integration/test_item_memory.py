"""Bảng nhớ món riêng theo người dùng (``user_item_memory``, ``app.bill.item_memory``), trên Postgres thật."""

import uuid

import httpx
import pytest_asyncio
from sqlalchemy import text
from sqlalchemy.ext.asyncio import async_sessionmaker

from app.api.v1.bill import get_session_factory
from app.bill import item_memory
from app.core.anonymizer import sha256_hex
from app.core.config import settings
from app.main import app

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


# ---- route

@pytest_asyncio.fixture
async def bill_client(engine):
    factory = async_sessionmaker(engine, expire_on_commit=False)
    app.dependency_overrides[get_session_factory] = lambda: factory
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test", headers={"Authorization": f"Bearer {settings.internal_api_key}"}) as c:
        yield c
    app.dependency_overrides.pop(get_session_factory, None)


def entry(name: str, category: uuid.UUID, kind: str = "accepted") -> dict:
    return {"name": name, "category_id": str(category), "kind": kind}


async def test_memory_route_records_and_erase_route_deletes(bill_client, session):
    tea, rice = uuid.uuid4(), uuid.uuid4()
    body = {"user_id_hash": USER, "items": [entry("Trà sữa", tea, "corrected"), entry("Gạo ST25", rice)]}

    recorded = await bill_client.post("/api/v1/bill/items/memory", json=body)
    assert recorded.status_code == 200 and recorded.json() == {"recorded": 2}
    assert await item_memory.lookup(session, USER, ["tra sua", "gao st25"]) == {"tra sua": tea, "gao st25": rice}

    assert (await bill_client.post("/api/v1/bill/items/memory/erase", json={"user_id_hash": OTHER})).json() == {"deleted": 0}
    erased = await bill_client.post("/api/v1/bill/items/memory/erase", json={"user_id_hash": USER})
    assert erased.status_code == 200 and erased.json() == {"deleted": 2}
    assert await item_memory.lookup(session, USER, ["tra sua", "gao st25"]) == {}


async def test_memory_route_reports_only_rows_actually_written(bill_client):
    await bill_client.post("/api/v1/bill/items/memory", json={"user_id_hash": USER, "items": [entry("Trà sữa", uuid.uuid4(), "corrected")]})

    blocked = await bill_client.post("/api/v1/bill/items/memory", json={"user_id_hash": USER, "items": [entry("Trà sữa", uuid.uuid4())]})

    assert blocked.json() == {"recorded": 0}


async def test_memory_route_swallows_db_errors_but_erase_lets_them_out(monkeypatch, engine):
    async def broken(*args, **kwargs):
        raise RuntimeError("db down")

    factory = async_sessionmaker(engine, expire_on_commit=False)
    app.dependency_overrides[get_session_factory] = lambda: factory
    monkeypatch.setattr(item_memory, "record", broken)
    monkeypatch.setattr(item_memory, "delete_by_user_hash", broken)
    transport = httpx.ASGITransport(app=app, raise_app_exceptions=False)
    try:
        async with httpx.AsyncClient(transport=transport, base_url="http://test", headers={"Authorization": f"Bearer {settings.internal_api_key}"}) as c:
            ok = await c.post("/api/v1/bill/items/memory", json={"user_id_hash": USER, "items": [entry("Trà sữa", uuid.uuid4())]})
            erased = await c.post("/api/v1/bill/items/memory/erase", json={"user_id_hash": USER})
    finally:
        app.dependency_overrides.pop(get_session_factory, None)

    assert ok.status_code == 200 and ok.json() == {"recorded": 0}
    assert erased.status_code == 500


async def test_memory_routes_validate_the_request(bill_client):
    one = entry("Trà sữa", uuid.uuid4())
    for items in ([], [one] * 61, [{**one, "kind": "other"}]):
        assert (await bill_client.post("/api/v1/bill/items/memory", json={"user_id_hash": USER, "items": items})).status_code == 422
    assert (await bill_client.post("/api/v1/bill/items/memory", json={"user_id_hash": "short", "items": [one]})).status_code == 422
    assert (await bill_client.post("/api/v1/bill/items/memory/erase", json={"user_id_hash": "short"})).status_code == 422


async def test_memory_routes_require_the_internal_key():
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as anonymous:
        assert (await anonymous.post("/api/v1/bill/items/memory", json={"user_id_hash": USER, "items": [entry("Trà sữa", uuid.uuid4())]})).status_code == 401
        assert (await anonymous.post("/api/v1/bill/items/memory/erase", json={"user_id_hash": USER})).status_code == 401
