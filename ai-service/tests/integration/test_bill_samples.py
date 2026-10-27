"""Mẫu huấn luyện chờ nhãn trên Postgres thật: chỉ chữ đã lọc, nhãn cuối thắng, xóa theo người dùng, dọn mẫu chưa gắn nhãn."""

import asyncio
import uuid

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import text
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import async_sessionmaker

from app.api.v1.bill import get_session_factory
from app.bill import samples
from app.bill.samples import PendingSample
from app.core.config import settings
from app.main import app

HASH = "a" * 64
OTHER = "b" * 64


def sample(**kw) -> PendingSample:
    base = dict(scan_id=uuid.uuid4(), user_id_hash=HASH, merchant_name="Tạp hóa Đức Cường", lines=["Gao thom 5kg", "Tong cong 125.000"],
                predicted_slug="food", predicted_source="keyword")
    base.update(kw)
    return PendingSample(**base)


async def rows(session):
    return (await session.execute(text("SELECT scan_id, user_id_hash, merchant_key, lines, final_slug, CASE WHEN final_slug IS NULL THEN NULL WHEN predicted_slug = final_slug THEN 'confirmed' ELSE 'corrected' END AS label_kind FROM ml_training_samples"))).all()


async def test_add_pending_stores_row(session):
    s = sample()
    assert await samples.add_pending(session, s) is True
    (row,) = await rows(session)
    assert row.scan_id == s.scan_id and row.user_id_hash == HASH and row.final_slug is None
    assert row.merchant_key == "tap hoa duc cuong"  # khóa cửa hàng đã bỏ dấu, dùng để nhóm khi đo
    assert row.lines == "Gao thom 5kg\nTong cong 125.000"


@pytest.mark.parametrize("bad", [["Sdt 0912 345 678"], ["STK 1234567890123"], ["Ma 123-456-789"], ["so 12.345.678.901"]])
async def test_rejects_lines_with_long_digit_runs(session, bad):
    assert await samples.add_pending(session, sample(lines=["Gao thom", *bad])) is False
    assert await rows(session) == []


def test_money_amounts_are_not_mistaken_for_phone_numbers():
    assert samples.has_long_digit_run(["Tong cong 1.250.000", "Thue 8%: 9.520", "Don gia 125.000"]) is False
    assert samples.has_long_digit_run(["0912345678"]) is True


async def test_add_pending_is_idempotent_on_scan_id(session):
    s = sample()
    assert await samples.add_pending(session, s) is True
    assert await samples.add_pending(session, s) is True
    assert len(await rows(session)) == 1


async def test_add_pending_does_not_overwrite_or_claim_another_users_scan(session):
    s = sample()
    await samples.add_pending(session, s)
    assert await samples.add_pending(session, sample(scan_id=s.scan_id, user_id_hash=OTHER)) is False
    (row,) = await rows(session)
    assert row.user_id_hash == HASH


async def test_empty_or_oversized_lines_are_rejected(session):
    assert await samples.add_pending(session, sample(lines=[])) is False
    assert await samples.add_pending(session, sample(lines=["x"] * 201)) is False
    assert await rows(session) == []


async def test_label_confirmed_when_equal_prediction(session):
    s = sample(predicted_slug="food")
    await samples.add_pending(session, s)
    assert await samples.label(session, s.scan_id, HASH, "food") is True
    (row,) = await rows(session)
    assert (row.final_slug, row.label_kind) == ("food", "confirmed")


async def test_label_corrected_when_different(session):
    s = sample(predicted_slug="food")
    await samples.add_pending(session, s)
    await samples.label(session, s.scan_id, HASH, "shopping")
    (row,) = await rows(session)
    assert (row.final_slug, row.label_kind) == ("shopping", "corrected")


async def test_label_is_corrected_when_there_was_no_prediction(session):
    s = sample(predicted_slug=None, predicted_source=None)
    await samples.add_pending(session, s)
    await samples.label(session, s.scan_id, HASH, "food")
    assert (await rows(session))[0].label_kind == "corrected"


async def test_relabel_updates_same_row(session):
    s = sample(predicted_slug="food")
    await samples.add_pending(session, s)
    await samples.label(session, s.scan_id, HASH, "shopping")
    await samples.label(session, s.scan_id, HASH, "food")
    (row,) = await rows(session)
    assert (row.final_slug, row.label_kind) == ("food", "confirmed")  # nhãn cuối thắng, vẫn một hàng


async def test_label_requires_matching_user_hash(session):
    s = sample()
    await samples.add_pending(session, s)
    assert await samples.label(session, s.scan_id, OTHER, "shopping") is False
    assert (await rows(session))[0].final_slug is None


async def test_label_for_a_scan_that_was_never_stored_is_a_noop(session):
    assert await samples.label(session, uuid.uuid4(), HASH, "food") is False


@pytest.mark.parametrize("slug", ["income", "unknown", "nonsense"])
async def test_label_rejects_income_and_unknown_slugs(session, slug):
    s = sample()
    await samples.add_pending(session, s)
    assert await samples.label(session, s.scan_id, HASH, slug) is False
    assert (await rows(session))[0].final_slug is None


async def test_check_constraint_rejects_a_bad_slug_directly(session):
    s = sample()
    await samples.add_pending(session, s)
    with pytest.raises(IntegrityError):
        await session.execute(text("UPDATE ml_training_samples SET final_slug = 'income'"))
    await session.rollback()


async def test_delete_by_user_hash(session):
    mine, theirs = sample(), sample(user_id_hash=OTHER)
    await samples.add_pending(session, mine)
    await samples.add_pending(session, theirs)
    assert await samples.delete_by_user_hash(session, HASH) == 1
    assert [r.user_id_hash for r in await rows(session)] == [OTHER]


async def test_purge_removes_old_pending(session):
    old_pending, old_labeled, fresh_pending = sample(), sample(), sample()
    for s in (old_pending, old_labeled, fresh_pending):
        await samples.add_pending(session, s)
    await samples.label(session, old_labeled.scan_id, HASH, "food")
    await session.execute(text("UPDATE ml_training_samples SET created_at = now() - interval '8 days' WHERE scan_id IN (:a, :b)"),
                          {"a": old_pending.scan_id, "b": old_labeled.scan_id})
    await session.commit()
    assert await samples.purge_pending(session, days=7) == 1  # chỉ hàng CHƯA nhãn và cũ hơn 7 ngày
    assert {r.scan_id for r in await rows(session)} == {old_labeled.scan_id, fresh_pending.scan_id}


# ---- route

@pytest_asyncio.fixture
async def client(engine):
    factory = async_sessionmaker(engine, expire_on_commit=False)
    app.dependency_overrides[get_session_factory] = lambda: factory
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test", headers={"Authorization": f"Bearer {settings.internal_api_key}"}) as c:
        yield c
    app.dependency_overrides.pop(get_session_factory, None)


async def test_routes_store_then_label(client, session):
    sid = str(uuid.uuid4())
    body = {"scan_id": sid, "user_id_hash": HASH, "merchant_name": "Quán cơm Nhà Bếp Quê", "lines": ["Com suon", "Tong cong 90.000"],
            "total_amount": 90000, "predicted_slug": "food", "predicted_source": "keyword"}
    assert (await client.post("/api/v1/bill/samples", json=body)).json() == {"stored": True}
    res = await client.post(f"/api/v1/bill/samples/{sid}/label", json={"user_id_hash": HASH, "final_slug": "shopping"})
    assert res.json() == {"labeled": True}
    assert (await rows(session))[0].label_kind == "corrected"


async def test_routes_reject_long_digit_lines_without_error(client, session):
    body = {"scan_id": str(uuid.uuid4()), "user_id_hash": HASH, "lines": ["Sdt 0912345678"]}
    assert (await client.post("/api/v1/bill/samples", json=body)).json() == {"stored": False}
    assert await rows(session) == []


async def test_routes_validate_the_input(client):
    assert (await client.post("/api/v1/bill/samples", json={"scan_id": "x", "user_id_hash": HASH, "lines": ["a"]})).status_code == 422
    assert (await client.post("/api/v1/bill/samples", json={"scan_id": str(uuid.uuid4()), "user_id_hash": "short", "lines": ["a"]})).status_code == 422
    sid = uuid.uuid4()
    assert (await client.post(f"/api/v1/bill/samples/{sid}/label", json={"user_id_hash": HASH, "final_slug": "income"})).status_code == 422


async def test_route_requires_internal_key():
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as c:
        res = await c.post("/api/v1/bill/samples", json={"scan_id": str(uuid.uuid4()), "user_id_hash": HASH, "lines": ["a"]})
    assert res.status_code == 401


async def test_erase_route_deletes_only_that_users_samples(client, session):
    mine, theirs = sample(), sample(user_id_hash=OTHER)
    await samples.add_pending(session, mine)
    await samples.add_pending(session, theirs)
    await samples.label(session, mine.scan_id, HASH, "food")  # đã có nhãn cũng bị xóa: đó chính là dữ liệu người dùng đã đóng góp

    res = await client.post("/api/v1/bill/samples/erase", json={"user_id_hash": HASH})

    assert res.json() == {"deleted": 1, "item_votes": 0}
    assert [r.user_id_hash for r in await rows(session)] == [OTHER]


async def test_erase_route_with_nothing_stored_deletes_zero(client):
    assert (await client.post("/api/v1/bill/samples/erase", json={"user_id_hash": HASH})).json() == {"deleted": 0, "item_votes": 0}


async def test_erase_route_validates_the_hash_and_the_key(client):
    assert (await client.post("/api/v1/bill/samples/erase", json={"user_id_hash": "short"})).status_code == 422
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as anonymous:
        assert (await anonymous.post("/api/v1/bill/samples/erase", json={"user_id_hash": HASH})).status_code == 401


async def test_add_pending_also_purges_old_unlabeled_samples(session):
    # không có scheduler: mỗi lần có mẫu mới thì dọn luôn mẫu chưa nhãn quá hạn, để lời hứa "xóa sau 7 ngày" tự đúng
    stale, kept_old_label, fresh = sample(), sample(), sample()
    await samples.add_pending(session, stale)
    await samples.add_pending(session, kept_old_label)
    await samples.label(session, kept_old_label.scan_id, HASH, "food")
    await session.execute(text("UPDATE ml_training_samples SET created_at = now() - interval '8 days'"))
    await session.commit()

    await samples.add_pending(session, fresh)

    assert {r.scan_id for r in await rows(session)} == {kept_old_label.scan_id, fresh.scan_id}


async def test_learning_report_reads_labeled_and_pending_from_db(session):
    import sys
    from pathlib import Path

    sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
    from learning_report import build_report

    labeled, pending = sample(), sample(merchant_name="Cửa hàng khác")
    for s in (labeled, pending):
        await samples.add_pending(session, s)
    await samples.label(session, labeled.scan_id, HASH, "food")

    report = await build_report(session)

    assert (report.labeled, report.pending, report.merchants) == (1, 1, 1)
    assert report.per_slug == {"food": 1} and report.accuracy_by_source == {"keyword": (1, 1.0)}


@pytest.mark.parametrize(
    "row",
    ["Com suon 2 45.000 90.000", "1 45.000 45.000", "Ca phe sua 29.000 1 29.000", "Mon A 1 20.000 20.000", "14-10-2026 10:32:05", "10.000.000"],
)
def test_real_item_rows_are_not_mistaken_for_long_numbers(row):
    # cột số lượng/đơn giá/thành tiền cách nhau bằng khoảng trắng: cộng chữ số qua khoảng trắng sẽ loại gần hết hóa đơn có nhiều món
    assert samples.has_long_digit_run([row]) is False


@pytest.mark.parametrize("row", ["Sdt 0912 345 678", "Lien he +84 912 345 678", "STK 1234567890123", "so 12.345.678.901", "Ma 123-456-789"])
def test_phone_and_account_numbers_are_still_rejected(row):
    assert samples.has_long_digit_run([row]) is True


async def test_purge_loop_removes_stale_pending_without_any_new_insert(session):
    # lời hứa "xóa sau 7 ngày" không được phụ thuộc vào việc có người quét tiếp
    stale = sample()
    await samples.add_pending(session, stale)
    await session.execute(text("UPDATE ml_training_samples SET created_at = now() - interval '8 days'"))
    await session.commit()
    task = asyncio.create_task(samples.purge_loop(async_sessionmaker(session.bind, expire_on_commit=False), interval_seconds=0.05))

    for _ in range(40):
        await asyncio.sleep(0.05)
        if not await rows(session):
            break
    task.cancel()

    assert await rows(session) == []


async def test_discard_removes_the_owners_sample_labeled_or_not(session):
    # người dùng chuyển sang danh mục tự tạo/thu nhập: nhãn hệ thống cũ là nhãn họ vừa bác bỏ, không được giữ làm dữ liệu học
    labeled, pending, someone_elses = sample(), sample(), sample(user_id_hash=OTHER)
    for s in (labeled, pending, someone_elses):
        await samples.add_pending(session, s)
    await samples.label(session, labeled.scan_id, HASH, "food")

    assert await samples.discard(session, labeled.scan_id, HASH) is True
    assert await samples.discard(session, pending.scan_id, OTHER) is False  # không xóa được mẫu của người khác
    assert await samples.discard(session, uuid.uuid4(), HASH) is False

    assert {r.scan_id for r in await rows(session)} == {pending.scan_id, someone_elses.scan_id}


async def test_discard_route(client, session):
    s = sample()
    await samples.add_pending(session, s)
    res = await client.post(f"/api/v1/bill/samples/{s.scan_id}/discard", json={"user_id_hash": HASH})
    assert res.json() == {"discarded": True}
    assert await rows(session) == []
