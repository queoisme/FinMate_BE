"""Phiếu danh mục của từng món (``ml_item_feedback``) và việc đưa món đủ phiếu vào ``kb_item_categories`` (``app.bill.item_votes``), trên Postgres thật."""

import httpx
import pytest_asyncio
from sqlalchemy import text
from sqlalchemy.ext.asyncio import async_sessionmaker

from app.api.v1.bill import get_session_factory
from app.bill import item_votes
from app.core.anonymizer import sha256_hex
from app.core.config import settings
from app.main import app

ACTOR = sha256_hex("admin")


def user(number: int) -> str:
    return sha256_hex(f"user-{number}")


@pytest_asyncio.fixture
async def bill_client(engine):
    factory = async_sessionmaker(engine, expire_on_commit=False)
    app.dependency_overrides[get_session_factory] = lambda: factory
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test", headers={"Authorization": f"Bearer {settings.internal_api_key}"}) as c:
        yield c
    app.dependency_overrides.pop(get_session_factory, None)


async def vote(session, number: int, name: str, slug: str) -> int:
    return await item_votes.record_votes(session, user(number), [(name, slug)])


async def rows(session):
    return [tuple(r) for r in (await session.execute(text("SELECT item_key, category_slug FROM kb_item_categories ORDER BY item_key"))).all()]


async def test_one_vote_per_person_per_item_and_a_later_vote_replaces_the_earlier(session):
    assert await vote(session, 1, "Khăn ướt Bobby", "shopping") == 1
    assert await vote(session, 1, "KHĂN ƯỚT bobby", "family") == 1  # cùng khóa (bỏ dấu, chữ thường): thay phiếu, không thêm

    stored = (await session.execute(text("SELECT item_key, category_slug FROM ml_item_feedback"))).all()
    assert [tuple(r) for r in stored] == [("khan uot bobby", "family")]


async def test_unusable_names_slugs_and_long_digit_runs_are_skipped_not_errors(session):
    recorded = await item_votes.record_votes(session, user(1), [
        ("SP002993", "shopping"),          # giống mã hàng
        ("ab", "shopping"),                # quá ngắn
        ("Bột giặt 0912345678", "shopping"),  # còn số điện thoại
        ("Bột giặt Omo", "income"),        # không phải khoản chi
        ("Bột giặt Omo", "khong-co"),      # slug lạ
        ("Bột giặt Omo", "shopping"),      # hợp lệ
    ])

    assert recorded == 1
    assert (await session.execute(text("SELECT count(*) FROM ml_item_feedback"))).scalar_one() == 1


async def test_an_item_with_enough_agreeing_people_is_added_with_source_consensus_and_audited(session):
    for number in (1, 2, 3):
        await vote(session, number, "Khăn ướt Bobby", "family")

    plan = await item_votes.promote_consensus(session, actor_hash=ACTOR)  # mặc định xem trước
    assert plan.promoted == ["khan uot bobby"] and await rows(session) == []

    done = await item_votes.promote_consensus(session, actor_hash=ACTOR, write=True)
    assert done.promoted == ["khan uot bobby"]
    assert await rows(session) == [("khan uot bobby", "family")]
    source = (await session.execute(text("SELECT source FROM kb_item_categories"))).scalar_one()
    audit = (await session.execute(text("SELECT resource, action, actor_hash FROM kb_audit_log"))).one()
    assert source == "consensus" and tuple(audit) == ("item_cache", "create", ACTOR)

    again = await item_votes.promote_consensus(session, actor_hash=ACTOR, write=True)  # chạy lại không thêm gì
    assert again.promoted == [] and len(await rows(session)) == 1


async def test_too_few_people_or_a_split_vote_is_not_enough(session):
    await vote(session, 1, "Sữa rửa mặt", "shopping")
    await vote(session, 2, "Sữa rửa mặt", "shopping")                  # 2 người: chưa đủ 3
    for number, slug in ((1, "shopping"), (2, "shopping"), (3, "health")):
        await vote(session, number, "Kem chống nắng", slug)           # 3 người nhưng 2/3 = 67% < 80%

    plan = await item_votes.promote_consensus(session, actor_hash=ACTOR, write=True)

    assert plan.promoted == [] and sorted(plan.pending) == ["kem chong nang", "sua rua mat"]
    assert await rows(session) == []


async def test_an_existing_row_is_never_overwritten_and_disagreement_is_reported(session):
    await session.execute(text("INSERT INTO kb_item_categories (item_key, category_slug, source) VALUES ('khan uot bobby', 'shopping', 'admin')"))
    await session.commit()
    for number in (1, 2, 3):
        await vote(session, number, "Khăn ướt Bobby", "family")

    result = await item_votes.promote_consensus(session, actor_hash=ACTOR, write=True)

    assert result.disputes == ["khan uot bobby"] and result.promoted == []
    assert await rows(session) == [("khan uot bobby", "shopping")]  # giữ nguyên dòng của quản trị viên


async def test_thresholds_are_parameters_not_constants(session):
    for number in (1, 2):
        await vote(session, number, "Khăn ướt Bobby", "family")

    result = await item_votes.promote_consensus(session, actor_hash=ACTOR, min_users=2, write=True)

    assert result.promoted == ["khan uot bobby"]


async def test_route_records_votes_and_reports_how_many_were_usable(bill_client, session):
    body = {"user_id_hash": user(1), "items": [{"name": "Khăn ướt Bobby", "category_slug": "family"}, {"name": "SP002993", "category_slug": "shopping"}]}

    response = await bill_client.post("/api/v1/bill/items/feedback", json=body)

    assert response.status_code == 200 and response.json() == {"recorded": 1}
    assert (await session.execute(text("SELECT count(*) FROM ml_item_feedback"))).scalar_one() == 1


async def test_route_validates_the_request_and_needs_the_internal_key(bill_client):
    ok = {"name": "Khăn ướt Bobby", "category_slug": "family"}
    assert (await bill_client.post("/api/v1/bill/items/feedback", json={"user_id_hash": "short", "items": [ok]})).status_code == 422
    assert (await bill_client.post("/api/v1/bill/items/feedback", json={"user_id_hash": user(1), "items": []})).status_code == 422
    assert (await bill_client.post("/api/v1/bill/items/feedback", json={"user_id_hash": user(1), "items": [{"name": "x" * 3, "category_slug": "income"}]})).status_code == 422
    anonymous = httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test")
    async with anonymous:
        assert (await anonymous.post("/api/v1/bill/items/feedback", json={"user_id_hash": user(1), "items": [ok]})).status_code == 401


async def test_erasing_a_user_removes_their_item_votes_too(bill_client, session):
    await vote(session, 1, "Khăn ướt Bobby", "family")
    await vote(session, 2, "Khăn ướt Bobby", "family")

    response = await bill_client.post("/api/v1/bill/samples/erase", json={"user_id_hash": user(1)})

    assert response.json() == {"deleted": 0, "item_votes": 1}
    remaining = (await session.execute(text("SELECT user_id_hash FROM ml_item_feedback"))).scalars().all()
    assert remaining == [user(2)]
