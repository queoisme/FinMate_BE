"""Hợp đồng bằng chứng của agent (Task 2): tiền luôn là số nguyên đồng, độ tin cậy nằm trong [0, 1], không nhận trường lạ."""

from datetime import datetime, timezone

import pytest
from pydantic import ValidationError

from app.agent.schemas import Evidence

AS_OF = datetime(2026, 11, 7, 3, 0, tzinfo=timezone.utc)


def _evidence(**overrides) -> dict:
    base = {"id": "e1", "key": "balance", "value_vnd": 1_500_000, "unit": "VND", "source": "confirmed", "as_of": AS_OF, "confidence": 0.9}
    return {**base, **overrides}


def test_valid_evidence_round_trips():
    evidence = Evidence(**_evidence(source_name="Sổ cái", source_url=None))
    assert evidence.value_vnd == 1_500_000
    assert Evidence.model_validate_json(evidence.model_dump_json()) == evidence


def test_evidence_rejects_float_money():
    for bad in (1_500_000.5, 1_500_000.0, "1500000"):
        with pytest.raises(ValidationError):
            Evidence(**_evidence(value_vnd=bad))


def test_evidence_text_only_has_no_money():
    evidence = Evidence(**_evidence(value_vnd=None, text="Giá xe tăng 5%/năm", source="document"))
    assert evidence.value_vnd is None


def test_confidence_must_be_between_zero_and_one():
    for bad in (-0.01, 1.01):
        with pytest.raises(ValidationError):
            Evidence(**_evidence(confidence=bad))


def test_unknown_source_and_extra_fields_are_rejected():
    with pytest.raises(ValidationError):
        Evidence(**_evidence(source="vibes"))
    with pytest.raises(ValidationError):
        Evidence(**_evidence(user_id="3f2504e0-4f89-11d3-9a0c-0305e82c3301"))
