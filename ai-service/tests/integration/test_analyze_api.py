"""``POST /api/v1/analyze`` chạy thật, trên Postgres thật."""

import uuid
from datetime import timedelta

import pytest

MB = "com.mbmobile"
MOMO = "com.mservice.momotransfer"

MB_DEBIT = "TK 0123456789|GD: -75,000VND 09/09/2026 10:28|SD: 2,500,000VND|ND: TT QR HIGHLANDS COFFEE"
MOMO_SAME_PAYMENT = "Bạn đã thanh toán 75.000đ cho Highlands Coffee. Số dư: 2.500.000đ"
OTP = "Ma OTP cua quy khach la 483920. Ma co hieu luc trong 3 phut, tuyet doi khong chia se."


def payload(
    user_hash, when, package=MB, body=MB_DEBIT, title="MB Bank", request_id=None
):
    return {
        "backend_request_id": str(request_id or uuid.uuid4()),
        "user_id_hash": user_hash,
        "package_name": package,
        "notification_title": title,
        "notification_body": body,
        "received_at": when.isoformat(),
    }


async def test_missing_api_key_is_rejected(client, user_hash, now):
    response = await client.post("/api/v1/analyze", json=payload(user_hash, now))
    assert response.status_code == 401


async def test_wrong_api_key_is_rejected(client, user_hash, now):
    response = await client.post(
        "/api/v1/analyze",
        json=payload(user_hash, now),
        headers={"Authorization": "Bearer sai-hoan-toan"},
    )
    assert response.status_code == 401


async def test_response_matches_the_backend_contract(
    client, auth_headers, user_hash, now
):
    """Từng field phải khớp AIServiceApiContracts.cs. Lệch tên không nổ exception ở đâu —
    System.Text.Json chỉ để field đó null."""
    response = await client.post(
        "/api/v1/analyze", json=payload(user_hash, now), headers=auth_headers
    )
    assert response.status_code == 200
    body = response.json()

    assert set(body) == {
        "pipeline_result",
        "classifier",
        "extraction",
        "categorization",
        "duplicate",
        "model_versions",
        "processing_ms",
    }
    assert set(body["classifier"]) == {"label", "confidence"}
    assert set(body["extraction"]) == {
        "amount_cents",
        "transaction_type",
        "merchant_name",
        "description",
        "transacted_at",
        "balance_after_cents",
        "confidence",
    }
    assert set(body["categorization"]) == {"category_slug", "confidence"}
    assert set(body["duplicate"]) == {"is_potential_duplicate", "duplicate_request_id"}
    assert set(body["model_versions"]) == {"classifier", "extractor", "categorizer"}


async def test_bank_notification_becomes_a_full_extraction(
    client, auth_headers, user_hash, now
):
    body = (
        await client.post(
            "/api/v1/analyze", json=payload(user_hash, now), headers=auth_headers
        )
    ).json()

    assert body["pipeline_result"] == "financial"
    assert body["extraction"]["amount_cents"] == 75_000
    assert body["extraction"]["transaction_type"] == "debit"
    assert body["extraction"]["merchant_name"] == "HIGHLANDS COFFEE"
    assert body["extraction"]["balance_after_cents"] == 2_500_000
    assert body["categorization"]["category_slug"] == "food"
    assert body["model_versions"]["extractor"] == "rule-1.0.0"
    assert body["processing_ms"] is not None


async def test_category_slug_is_always_a_system_category(
    client, auth_headers, user_hash, now
):
    """Backend tra slug bằng GetSystemBySlugAsync — slug lạ trả NULL và giao dịch mất
    danh mục mà không có lỗi nào."""
    from app.core.enums import CATEGORY_SLUGS

    body = (
        await client.post(
            "/api/v1/analyze", json=payload(user_hash, now), headers=auth_headers
        )
    ).json()
    assert body["categorization"]["category_slug"] in CATEGORY_SLUGS


async def test_promotional_notification_stops_before_extraction(
    client, auth_headers, user_hash, now
):
    body = (
        await client.post(
            "/api/v1/analyze",
            json=payload(user_hash, now, body=OTP),
            headers=auth_headers,
        )
    ).json()

    assert body["pipeline_result"] == "non_financial"
    assert body["extraction"] is None
    assert body["categorization"] is None


async def test_same_payment_reported_by_two_apps_is_flagged_as_duplicate(
    client, auth_headers, user_hash, now
):
    """Một lần quẹt thẻ, hai thông báo: một từ app ngân hàng, một từ ví liên kết. Nội dung
    khác hẳn nhau nên hash nội dung phía backend không bắt được — thứ trùng là số tiền và
    thời điểm (docx Flow 1 bước 4.3)."""
    first_id = uuid.uuid4()
    first = await client.post(
        "/api/v1/analyze",
        json=payload(user_hash, now, request_id=first_id),
        headers=auth_headers,
    )
    second = await client.post(
        "/api/v1/analyze",
        json=payload(
            user_hash,
            now + timedelta(minutes=2),
            package=MOMO,
            body=MOMO_SAME_PAYMENT,
            title="MoMo",
        ),
        headers=auth_headers,
    )

    assert first.json()["duplicate"]["is_potential_duplicate"] is False
    duplicate = second.json()["duplicate"]
    assert duplicate["is_potential_duplicate"] is True
    # Trả backend_request_id của thông báo trước, không phải id nội bộ của AI DB —
    # backend chỉ tra ngược được theo id notification_log của chính nó.
    assert duplicate["duplicate_request_id"] == str(first_id)


async def test_same_amount_outside_the_five_minute_window_is_not_a_duplicate(
    client, auth_headers, user_hash, now
):
    await client.post(
        "/api/v1/analyze", json=payload(user_hash, now), headers=auth_headers
    )
    later = await client.post(
        "/api/v1/analyze",
        json=payload(
            user_hash,
            now + timedelta(minutes=30),
            package=MOMO,
            body=MOMO_SAME_PAYMENT,
            title="MoMo",
        ),
        headers=auth_headers,
    )
    assert later.json()["duplicate"]["is_potential_duplicate"] is False


async def test_duplicate_detection_does_not_cross_users(client, auth_headers, now):
    from app.core.anonymizer import sha256_hex

    one = sha256_hex("nguoi-dung-1")
    two = sha256_hex("nguoi-dung-2")

    await client.post("/api/v1/analyze", json=payload(one, now), headers=auth_headers)
    other = await client.post(
        "/api/v1/analyze",
        json=payload(
            two, now + timedelta(minutes=1), package=MOMO, body=MOMO_SAME_PAYMENT
        ),
        headers=auth_headers,
    )
    assert other.json()["duplicate"]["is_potential_duplicate"] is False


async def test_retrying_the_same_request_id_returns_the_stored_result(
    client, auth_headers, user_hash, now
):
    """RetryFailedNotificationJob phía backend gửi lại đúng request cũ. Chạy lại pipeline
    sẽ tự dò trùng với chính dòng của mình và sinh ra một giao dịch nháp thứ hai."""
    request_id = uuid.uuid4()
    body = payload(user_hash, now, request_id=request_id)

    first = await client.post("/api/v1/analyze", json=body, headers=auth_headers)
    second = await client.post("/api/v1/analyze", json=body, headers=auth_headers)

    assert first.json() == second.json()
    assert second.json()["duplicate"]["is_potential_duplicate"] is False


async def test_natural_language_entry_is_parsed(client, auth_headers, user_hash, now):
    body = (
        await client.post(
            "/api/v1/analyze",
            json=payload(
                user_hash,
                now,
                package="manual_entry",
                body="trưa nay ăn phở 45k ở Phở Thìn",
                title=None,
            ),
            headers=auth_headers,
        )
    ).json()

    assert body["pipeline_result"] == "financial"
    assert body["extraction"]["amount_cents"] == 45_000
    assert body["categorization"]["category_slug"] == "food"


async def test_every_run_is_logged_with_an_anonymised_sample(
    client, auth_headers, user_hash, now, session
):
    from sqlalchemy import select

    from app.db.models.pipeline_request import PipelineRequest
    from app.db.models.raw_sample import RawSample

    request_id = uuid.uuid4()
    await client.post(
        "/api/v1/analyze",
        json=payload(user_hash, now, request_id=request_id),
        headers=auth_headers,
    )

    row = (
        await session.execute(
            select(PipelineRequest).where(
                PipelineRequest.backend_request_id == request_id
            )
        )
    ).scalar_one()
    assert row.matched_pattern_name == "mb_qr_v1"
    assert row.raw_sample_id is not None

    sample = (
        await session.execute(
            select(RawSample).where(RawSample.id == row.raw_sample_id)
        )
    ).scalar_one()
    assert "0123456789" not in sample.notification_body
    assert "75,000VND" in sample.notification_body


@pytest.mark.parametrize(
    "broken",
    [
        {"user_id_hash": "qua-ngan"},
        {"notification_body": ""},
        {"backend_request_id": "khong-phai-uuid"},
    ],
)
async def test_malformed_requests_are_rejected(
    client, auth_headers, user_hash, now, broken
):
    body = payload(user_hash, now) | broken
    response = await client.post("/api/v1/analyze", json=body, headers=auth_headers)
    assert response.status_code == 422


async def test_transacted_at_is_returned_in_utc(client, auth_headers, user_hash, now):
    """Npgsql TỪ CHỐI ghi DateTimeOffset có offset khác 0 vào cột timestamptz, và backend
    đưa thẳng giá trị này vào transactions.transacted_at. Trả "+07:00" làm mọi thông báo
    tài chính đổ 500 ở phía backend — lỗi nằm ngoài AI Service nên test của riêng nó không
    bao giờ đỏ. Cùng cái bẫy đã ghi ở TASKS.md Phase 5, ở đầu bên kia contract.
    """
    from datetime import datetime, timedelta

    body = (
        await client.post(
            "/api/v1/analyze", json=payload(user_hash, now), headers=auth_headers
        )
    ).json()

    parsed = datetime.fromisoformat(body["extraction"]["transacted_at"])
    assert parsed.utcoffset() == timedelta(0)
    # Vẫn đúng thời điểm, chỉ khác cách biểu diễn.
    assert parsed.astimezone(now.tzinfo).hour == 10
