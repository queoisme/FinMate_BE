"""``GET /admin/learning/feedback``: server cắt trang, tìm kiếm và lọc; ``%`` gõ vào là chữ; không bao giờ lộ băm người dùng hay mã giao dịch."""

from datetime import datetime, timedelta, timezone

from app.db.models.user_feedback import UserFeedback

BASE = "/api/v1/admin/learning"
USER = "a" * 64
TXN = "c" * 64
NOW = datetime(2026, 11, 1, 10, 0, tzinfo=timezone.utc)


async def seed_feedback(session):
    session.add_all(
        UserFeedback(backend_transaction_id_hash=TXN, user_id_hash=USER, package_name=package, predicted_category=predicted,
                     corrected_category=corrected, feedback_type=kind, processed_at=processed, created_at=NOW + timedelta(minutes=i))
        for i, (package, predicted, corrected, kind, processed) in enumerate([
            ("com.vnpay.Agribank3g", "food", "transport", "category_correction", None),
            ("mobile.acb.com.vn", "food", None, "not_a_transaction", NOW),
            ("vn.abbank.retail", "transport", "food", "category_correction", NOW),
        ])
    )
    await session.commit()


async def test_feedback_pages_search_and_filters_and_hides_hashes(session, client, auth_headers):
    await seed_feedback(session)
    get = lambda **params: client.get(f"{BASE}/feedback", params=params, headers=auth_headers)

    page = (await get(limit=2)).json()
    assert (page["total"], len(page["items"])) == (3, 2) and page["items"][0]["package_name"] == "vn.abbank.retail"
    assert set(page["items"][0]) == {"id", "package_name", "predicted_category", "corrected_category", "feedback_type", "processed", "created_at"}
    assert USER not in str(page) and TXN not in str(page)
    assert (await get(q="agribank")).json()["total"] == 1  # theo gói ứng dụng
    assert (await get(q="transport")).json()["total"] == 2  # theo danh mục đoán hoặc sửa
    assert (await get(feedback_type="not_a_transaction")).json()["total"] == 1
    assert (await get(processed="true")).json()["total"] == 2 and (await get(processed="false")).json()["total"] == 1
    assert (await get(feedback_type="other")).status_code == 422


async def test_learning_lists_need_the_internal_key(client):
    assert (await client.get(f"{BASE}/feedback")).status_code == 401
