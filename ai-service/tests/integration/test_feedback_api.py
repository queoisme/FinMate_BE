"""``POST /api/v1/feedback`` và vòng phản hồi về dataset."""

import uuid

from sqlalchemy import select

from app.core.anonymizer import sha256_hex
from app.db.models.user_feedback import UserFeedback

MB = "com.mbmobile"
MB_DEBIT = "TK 0123456789|GD: -75,000VND 09/09/2026 10:28|SD: 2,500,000VND|ND: TT QR HIGHLANDS COFFEE"


def feedback_payload(user_hash, **overrides):
    return {
        "backend_transaction_id_hash": sha256_hex(str(uuid.uuid4())),
        "user_id_hash": user_hash,
        "pipeline_request_id": None,
        "package_name": MB,
        "predicted_category": "food",
        "corrected_category": "entertainment",
        "feedback_type": "category_correction",
    } | overrides


async def analyze(client, auth_headers, user_hash, now, request_id):
    return await client.post(
        "/api/v1/analyze",
        json={
            "backend_request_id": str(request_id),
            "user_id_hash": user_hash,
            "package_name": MB,
            "notification_title": "MB Bank",
            "notification_body": MB_DEBIT,
            "received_at": now.isoformat(),
        },
        headers=auth_headers,
    )


async def test_feedback_requires_the_internal_key(client, user_hash):
    response = await client.post("/api/v1/feedback", json=feedback_payload(user_hash))
    assert response.status_code == 401


async def test_feedback_is_stored(client, auth_headers, user_hash, session):
    response = await client.post(
        "/api/v1/feedback", json=feedback_payload(user_hash), headers=auth_headers
    )
    assert response.status_code == 200

    row = (await session.execute(select(UserFeedback))).scalar_one()
    assert row.predicted_category == "food"
    assert row.corrected_category == "entertainment"
    assert row.processed_at is None


async def test_feedback_links_back_to_the_prediction_it_corrects(
    client, auth_headers, user_hash, now, session
):
    """Backend hiện truyền pipeline_request_id = null, nên phải dò ngược bằng
    (user, package, danh mục đã đoán). Không liên kết được thì phản hồi không bao giờ
    thành nhãn huấn luyện."""
    request_id = uuid.uuid4()
    await analyze(client, auth_headers, user_hash, now, request_id)

    await client.post(
        "/api/v1/feedback", json=feedback_payload(user_hash), headers=auth_headers
    )

    row = (await session.execute(select(UserFeedback))).scalar_one()
    assert row.pipeline_request_id is not None


async def test_explicit_pipeline_request_id_is_resolved(
    client, auth_headers, user_hash, now, session
):
    """Đường sạch: backend truyền notification_log id vào trường contract đã có sẵn."""
    request_id = uuid.uuid4()
    await analyze(client, auth_headers, user_hash, now, request_id)

    await client.post(
        "/api/v1/feedback",
        json=feedback_payload(user_hash, pipeline_request_id=str(request_id)),
        headers=auth_headers,
    )

    row = (await session.execute(select(UserFeedback))).scalar_one()
    assert row.pipeline_request_id is not None


async def test_user_created_category_is_dropped_instead_of_crashing(
    client, auth_headers, user_hash, session
):
    """Danh mục tự tạo của người dùng có slug tuỳ ý. Ghi thẳng vào DB sẽ vi phạm CHECK
    constraint và làm request 500 — trong khi backend gọi feedback theo kiểu best-effort
    nên sẽ nuốt lỗi và không ai biết dữ liệu đang mất."""
    response = await client.post(
        "/api/v1/feedback",
        json=feedback_payload(user_hash, corrected_category="quy-den-cua-toi"),
        headers=auth_headers,
    )
    assert response.status_code == 200

    row = (await session.execute(select(UserFeedback))).scalar_one()
    assert row.corrected_category is None
    assert row.predicted_category == "food"


async def test_unknown_feedback_type_is_normalised(
    client, auth_headers, user_hash, session
):
    response = await client.post(
        "/api/v1/feedback",
        json=feedback_payload(user_hash, feedback_type="chua-tung-co"),
        headers=auth_headers,
    )
    assert response.status_code == 200

    row = (await session.execute(select(UserFeedback))).scalar_one()
    assert row.feedback_type == "category_correction"


async def test_feedback_becomes_a_training_label(
    client, auth_headers, user_hash, now, session
):
    """Khâu khép vòng: người dùng sửa danh mục → nhãn mới trong labeled_samples → lần
    train sau model học được đúng chỗ nó sai."""
    import sys
    from pathlib import Path

    sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
    from feedback_batch import _apply

    from app.db.models.labeled_sample import LabeledSample
    from app.db.models.pipeline_request import PipelineRequest

    request_id = uuid.uuid4()
    await analyze(client, auth_headers, user_hash, now, request_id)
    await client.post(
        "/api/v1/feedback", json=feedback_payload(user_hash), headers=auth_headers
    )

    feedback = (await session.execute(select(UserFeedback))).scalar_one()
    assert await _apply(session, feedback, now) is True
    await session.commit()

    label = (await session.execute(select(LabeledSample))).scalar_one()
    assert label.category_slug == "entertainment"
    assert label.labeled_by == "user_feedback"
    assert label.is_gold is True
    assert label.amount_cents == 75_000

    request = (await session.execute(select(PipelineRequest))).scalar_one()
    assert label.raw_sample_id == request.raw_sample_id
