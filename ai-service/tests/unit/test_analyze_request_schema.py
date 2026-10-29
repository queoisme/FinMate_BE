"""``allow_ai`` là trường tùy chọn mặc định false: backend cũ không gửi thì không bao giờ gọi AI."""

import uuid
from datetime import UTC, datetime

from app.schemas.request import AnalyzeRequest

BASE = {
    "backend_request_id": str(uuid.uuid4()),
    "user_id_hash": "a" * 64,
    "package_name": "com.x",
    "notification_body": "abc",
    "received_at": datetime(2026, 10, 29, 9, 15, tzinfo=UTC).isoformat(),
}


def test_allow_ai_defaults_to_false_for_old_json():
    assert AnalyzeRequest.model_validate(BASE).allow_ai is False


def test_allow_ai_true_is_accepted():
    assert AnalyzeRequest.model_validate(BASE | {"allow_ai": True}).allow_ai is True


def test_unknown_fields_are_still_ignored():
    assert AnalyzeRequest.model_validate(BASE | {"future_field": 1}).allow_ai is False
