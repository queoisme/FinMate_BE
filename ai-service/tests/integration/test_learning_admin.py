"""``GET /admin/learning/unclear|feedback``: server cắt trang, tìm kiếm và lọc; ``%`` gõ vào là chữ; không bao giờ lộ băm người dùng hay mã giao dịch."""

from datetime import datetime, timedelta, timezone

from app.db.models.nlu_candidate import NluCandidate
from app.db.models.user_feedback import UserFeedback

BASE = "/api/v1/admin/learning"
USER = "a" * 64
TXN = "c" * 64
NOW = datetime(2026, 11, 1, 10, 0, tzinfo=timezone.utc)


async def seed_unclear(session):
    session.add_all(
        NluCandidate(user_id_hash=USER, text=text, text_key=text, predicted_intent="out_of_scope", predicted_confidence=0.5,
                     unclear_reason=reason, status=status, created_at=NOW + timedelta(minutes=i))
        for i, (text, reason, status) in enumerate([
            ("mai tôi trả bác kia chưa nhỉ", "not_understood", "pending"),
            ("giảm giá 100% hôm nay", "low_confidence", "pending"),
            ("ăn trưa với sếp", "disagree", "rejected"),
        ])
    )
    await session.commit()


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


async def test_unclear_pages_are_disjoint_newest_first_and_hide_the_user(session, client, auth_headers):
    await seed_unclear(session)
    first = (await client.get(f"{BASE}/unclear", params={"limit": 2}, headers=auth_headers)).json()
    second = (await client.get(f"{BASE}/unclear", params={"limit": 2, "offset": 2}, headers=auth_headers)).json()

    assert (first["total"], len(first["items"]), first["limit"], first["offset"]) == (3, 2, 2, 0)
    assert [i["text"] for i in first["items"]] == ["ăn trưa với sếp", "giảm giá 100% hôm nay"]  # mới nhất trước
    assert [i["text"] for i in second["items"]] == ["mai tôi trả bác kia chưa nhỉ"] and second["offset"] == 2
    assert set(first["items"][0]) == {"id", "text", "predicted_intent", "predicted_confidence", "unclear_reason", "status", "created_at"}
    assert USER not in str(first) and USER not in str(second)


async def test_unclear_search_and_filters_run_on_the_server(session, client, auth_headers):
    await seed_unclear(session)
    get = lambda **params: client.get(f"{BASE}/unclear", params=params, headers=auth_headers)

    assert (await get(q="BÁC KIA")).json()["total"] == 1  # không phân biệt hoa thường
    assert (await get(q="100%")).json()["total"] == 1 and (await get(q="%")).json()["total"] == 1  # % là chữ, không phải ký tự đại diện
    assert (await get(q="_")).json()["total"] == 0
    assert (await get(status="rejected")).json()["total"] == 1
    assert (await get(reason="low_confidence")).json()["items"][0]["text"] == "giảm giá 100% hôm nay"
    assert (await get(q="không có câu này")).json() == {"items": [], "total": 0, "limit": 20, "offset": 0}
    assert (await get(status="accepted")).status_code == 422  # ngoài danh sách trạng thái
    assert (await get(limit=0)).status_code == 422 and (await get(offset=-1)).status_code == 422


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
    assert (await client.get(f"{BASE}/unclear")).status_code == 401
    assert (await client.get(f"{BASE}/feedback")).status_code == 401
