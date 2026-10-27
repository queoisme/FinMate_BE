"""``ml_label_events``, ``ml_item_feedback`` và hai view đồng thuận (migration 0024) trên Postgres thật."""

import uuid

import pytest
from sqlalchemy import text
from sqlalchemy.exc import DBAPIError

H1, H2, H3 = "1" * 64, "2" * 64, "3" * 64


async def attempt(session, sql: str, **params) -> bool:
    """True nếu câu lệnh bị DB từ chối (ràng buộc hoặc trigger)."""
    try:
        await session.execute(text(sql), params)
        await session.commit()
        return False
    except DBAPIError:
        await session.rollback()
        return True


async def make_sample(session, *, financial: bool = True, category: str | None = "food") -> uuid.UUID:
    raw_id, label_id = uuid.uuid4(), uuid.uuid4()
    await session.execute(text("INSERT INTO ml_raw_samples (id, source, package_name, notification_body, received_at, content_hash) VALUES (:id, 'notification', 'p', 'x', now(), :h)"),
                          {"id": raw_id, "h": raw_id.hex * 2})
    await session.execute(text("INSERT INTO ml_labeled_samples (id, raw_sample_id, is_financial, category_slug, labeled_by) VALUES (:id, :raw, :f, :c, 'seed')"),
                          {"id": label_id, "raw": raw_id, "f": financial, "c": category if financial else None})
    await session.commit()
    return label_id


ADD_EVENT = "INSERT INTO ml_label_events (labeled_sample_id, category_slug, is_financial, source) VALUES (:s, :c, :f, :src)"


async def test_events_are_added_not_edited_and_cannot_be_touched_directly(session):
    sample = await make_sample(session)
    assert not await attempt(session, ADD_EVENT, s=sample, c="food", f=True, src="import")
    assert not await attempt(session, ADD_EVENT, s=sample, c="shopping", f=True, src="feedback")  # đổi nhãn = thêm sự kiện mới

    assert await attempt(session, "UPDATE ml_label_events SET category_slug = 'other'")
    assert await attempt(session, "DELETE FROM ml_label_events")
    assert (await session.execute(text("SELECT count(*) FROM ml_label_events"))).scalar_one() == 2  # cả hai lần thử chặn đều không làm mất gì
    assert set((await session.execute(text("SELECT category_slug FROM ml_label_events"))).scalars()) == {"food", "shopping"}


async def test_deleting_the_sample_still_removes_its_events(session):
    sample = await make_sample(session)
    await attempt(session, ADD_EVENT, s=sample, c="food", f=True, src="import")

    assert not await attempt(session, "DELETE FROM ml_raw_samples WHERE id = (SELECT raw_sample_id FROM ml_labeled_samples WHERE id = :s)", s=sample)

    assert (await session.execute(text("SELECT count(*) FROM ml_label_events"))).scalar_one() == 0  # quyền xóa dữ liệu không bị nhật ký cản


async def test_deleting_a_feedback_row_clears_the_link_without_touching_the_event(session):
    sample = await make_sample(session)
    feedback = uuid.uuid4()
    await session.execute(text("INSERT INTO ml_user_feedback (id, backend_transaction_id_hash, user_id_hash, package_name, feedback_type) VALUES (:id, repeat('a', 64), repeat('b', 64), 'p', 'category_correction')"), {"id": feedback})
    await session.execute(text("INSERT INTO ml_label_events (labeled_sample_id, category_slug, is_financial, source, feedback_id) VALUES (:s, 'food', true, 'feedback', :f)"), {"s": sample, "f": feedback})
    await session.commit()

    assert not await attempt(session, "DELETE FROM ml_user_feedback WHERE id = :f", f=feedback)

    row = (await session.execute(text("SELECT source, feedback_id FROM ml_label_events"))).one()
    assert (row.source, row.feedback_id) == ("feedback", None)


async def test_event_constraints(session):
    sample = await make_sample(session)
    assert await attempt(session, ADD_EVENT, s=sample, c="food", f=True, src="robot")  # nguồn lạ
    assert await attempt(session, ADD_EVENT, s=sample, c="food", f=False, src="user")  # không phải giao dịch thì không có danh mục
    assert await attempt(session, ADD_EVENT, s=sample, c="zzz", f=True, src="user")  # danh mục không có trong kb_categories
    assert await attempt(session, "INSERT INTO ml_label_events (labeled_sample_id, is_financial, source, actor_hash) VALUES (:s, true, 'user', 'not-a-hash')", s=sample)
    assert not await attempt(session, "INSERT INTO ml_label_events (labeled_sample_id, is_financial, source, actor_hash) VALUES (:s, true, 'user', :h)", s=sample, h=H1)
    assert not await attempt(session, ADD_EVENT, s=sample, c=None, f=False, src="system")


ADD_VOTE = "INSERT INTO ml_item_feedback (item_key, user_id_hash, category_slug) VALUES (:k, :u, :c)"


async def test_item_feedback_is_one_vote_per_user_per_item_with_clean_keys(session):
    assert not await attempt(session, ADD_VOTE, k="khan uot", u=H1, c="food")
    assert await attempt(session, ADD_VOTE, k="khan uot", u=H1, c="shopping")  # cùng người, cùng món: phiếu thứ hai bị từ chối (đổi ý thì UPDATE phiếu cũ)
    assert not await attempt(session, ADD_VOTE, k="khan uot", u=H2, c="shopping")
    assert await attempt(session, ADD_VOTE, k="Khan Uot", u=H3, c="food")  # khóa phải viết thường
    assert await attempt(session, ADD_VOTE, k="ab", u=H3, c="food")  # quá ngắn
    assert await attempt(session, ADD_VOTE, k="nuoc mam", u="short", c="food")  # mã băm không phải hex 64
    assert await attempt(session, ADD_VOTE, k="nuoc mam", u=H3, c="income")  # hóa đơn là tiền ra
    assert await attempt(session, ADD_VOTE, k="nuoc mam", u=H3, c="dining")  # dòng bí danh của nhóm cũ không được vào kiến thức
    assert await attempt(session, ADD_VOTE, k="nuoc mam", u=H3, c="zzz")


async def test_item_consensus_counts_distinct_users_per_category(session):
    for key, user, category in (("khan uot", H1, "food"), ("khan uot", H2, "food"), ("khan uot", H3, "shopping"), ("bot giat", H1, "shopping")):
        assert not await attempt(session, ADD_VOTE, k=key, u=user, c=category)

    rows = (await session.execute(text("SELECT item_key, category_slug, users, agreement FROM v_item_consensus ORDER BY item_key, users DESC"))).all()

    assert [(r.item_key, r.category_slug, r.users) for r in rows] == [("bot giat", "shopping", 1), ("khan uot", "food", 2), ("khan uot", "shopping", 1)]
    assert [round(r.agreement, 3) for r in rows] == [1.0, 0.667, 0.333]


async def sample_row(session, user: str, merchant: str | None, slug: str | None, *, age_days: int, unit: str = "bill") -> None:
    await session.execute(
        text("INSERT INTO ml_training_samples (scan_id, user_id_hash, merchant_key, lines, final_slug, sample_unit, created_at, labeled_at) "
             "VALUES (:id, :u, :m, 'x', :s, :unit, now() - make_interval(days => :age), CASE WHEN CAST(:s AS varchar) IS NULL THEN NULL ELSE now() - make_interval(days => :age) END)"),
        {"id": uuid.uuid4(), "u": user, "m": merchant, "s": slug, "unit": unit, "age": age_days},
    )


async def test_merchant_consensus_counts_one_latest_vote_per_user(session):
    await sample_row(session, H1, "highlands", "food", age_days=10)
    await sample_row(session, H1, "highlands", "shopping", age_days=1)  # H1 đổi ý: chỉ phiếu mới nhất tính
    await sample_row(session, H2, "highlands", "food", age_days=3)
    await sample_row(session, H3, "highlands", "food", age_days=2)
    await sample_row(session, H3, "highlands", None, age_days=0)  # chưa có nhãn cuối: bỏ qua
    await sample_row(session, H1, "highlands", "transport", age_days=0, unit="item")  # chỉ đếm bill, không đếm món
    await sample_row(session, H1, None, "food", age_days=0)  # không có khóa cửa hàng: bỏ qua
    await session.commit()

    rows = (await session.execute(text("SELECT merchant_key, category_slug, users, agreement FROM v_merchant_consensus ORDER BY users DESC"))).all()

    assert [(r.merchant_key, r.category_slug, r.users) for r in rows] == [("highlands", "food", 2), ("highlands", "shopping", 1)]
    assert [round(r.agreement, 3) for r in rows] == [0.667, 0.333]
    assert pytest.approx(sum(r.agreement for r in rows)) == 1.0  # các dòng của một cửa hàng cộng lại bằng 1
