"""ERD v5_1 giai đoạn 1 (migration 0021): tên bảng mới, ràng buộc mới và khóa ngoại tự tham chiếu, trên Postgres thật."""

import uuid
from datetime import UTC, datetime

import pytest
from sqlalchemy import select, text
from sqlalchemy.exc import IntegrityError

from app.core.enums import PipelineResult
from app.db.models.pipeline_request import PipelineRequest

NEW_NAMES = (
    "kb_brands", "kb_keywords", "kb_item_categories", "kb_ai_instructions", "kb_provider_patterns", "kb_audit_log",
    "user_merchant_memory", "ops_analyze_log", "ops_usage_daily", "ml_user_feedback", "ml_raw_samples",
    "ml_feedback_batches", "ml_sample_splits", "ml_training_samples",
)
OLD_NAMES = (
    "bill_brands", "bill_keywords", "bill_item_cache", "bill_ai_instructions", "provider_patterns", "bill_rule_audit",
    "bill_user_merchant_choice", "pipeline_requests", "bill_classify_stats", "user_feedback", "raw_samples",
    "feedback_batch_jobs", "sample_splits", "bill_training_samples",
)


def log_row(**overrides) -> PipelineRequest:
    fields = dict(
        backend_request_id=uuid.uuid4(), user_id_hash="a" * 64, package_name="com.test.bank",
        received_at=datetime.now(UTC), pipeline_result=PipelineResult.FINANCIAL.value,
    )
    return PipelineRequest(**{**fields, **overrides})


async def test_tables_use_the_new_names(session):
    present = set((await session.execute(text("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'"))).scalars())
    assert set(NEW_NAMES) <= present
    assert not set(OLD_NAMES) & present


async def test_feedback_column_is_batch_id(session):
    columns = set((await session.execute(text("SELECT column_name FROM information_schema.columns WHERE table_name = 'ml_user_feedback'"))).scalars())
    assert "batch_id" in columns and "batch_job_id" not in columns


@pytest.mark.parametrize("overrides", [
    {"classifier_confidence": 1.5},
    {"extraction_confidence": -0.1},
    {"categorization_confidence": 2.0},
    {"amount_cents": -1},
    {"processing_ms": -5},
    {"categorizer_fallback_reason": "gemini_down"},
    {"extra_fields": ["not", "an", "object"]},
])
async def test_ops_analyze_log_rejects_out_of_range_values(session, overrides):
    session.add(log_row(**overrides))
    with pytest.raises(IntegrityError):
        await session.commit()


async def test_ops_analyze_log_accepts_the_new_columns(seeded_session):
    session = seeded_session
    pattern_id = (await session.execute(text("SELECT id FROM kb_provider_patterns LIMIT 1"))).scalar_one()  # pattern_id là khóa ngoại: phải trỏ vào pattern có thật
    session.add(log_row(categorizer_fallback_reason="timeout", pattern_id=pattern_id, config_versions={"chat_dictionary": 3}, extra_fields={"channel": "atm"}))
    await session.commit()
    row = (await session.execute(select(PipelineRequest))).scalar_one()
    assert (row.categorizer_fallback_reason, row.extra_fields) == ("timeout", {"channel": "atm"})


async def test_deleting_the_original_request_clears_the_duplicate_pointer(session):
    parent = log_row()
    session.add(parent)
    await session.commit()
    child = log_row(is_potential_duplicate=True, duplicate_of_request_id=parent.id)
    session.add(child)
    await session.commit()

    await session.execute(text("DELETE FROM ops_analyze_log WHERE id = :id"), {"id": parent.id})
    await session.commit()

    await session.refresh(child)
    assert child.duplicate_of_request_id is None


async def test_duplicate_pointer_must_reference_an_existing_row(session):
    session.add(log_row(duplicate_of_request_id=uuid.uuid4()))
    with pytest.raises(IntegrityError):
        await session.commit()


async def test_item_category_source_defaults_to_ai_and_is_constrained(session):
    await session.execute(text("INSERT INTO kb_item_categories (item_key, category_slug) VALUES ('khan uot', 'food')"))
    await session.commit()
    assert (await session.execute(text("SELECT source FROM kb_item_categories WHERE item_key = 'khan uot'"))).scalar_one() == "ai"

    for key, source in (("bot giat", "bogus"), ("ab", "ai")):  # nguồn lạ; khóa ngắn hơn 3 ký tự
        with pytest.raises(IntegrityError):
            await session.execute(text("INSERT INTO kb_item_categories (item_key, category_slug, source) VALUES (:k, 'food', :s)"), {"k": key, "s": source})
        await session.rollback()


async def test_training_sample_unit_defaults_to_bill(session):
    scan = uuid.uuid4()
    await session.execute(text("INSERT INTO ml_training_samples (scan_id, user_id_hash, lines) VALUES (:s, repeat('a', 64), 'x')"), {"s": scan})
    await session.commit()
    assert (await session.execute(text("SELECT sample_unit FROM ml_training_samples WHERE scan_id = :s"), {"s": scan})).scalar_one() == "bill"

    with pytest.raises(IntegrityError):
        await session.execute(text("INSERT INTO ml_training_samples (scan_id, user_id_hash, lines, sample_unit) VALUES (:s, repeat('a', 64), 'x', 'receipt')"), {"s": uuid.uuid4()})
    await session.rollback()


async def test_usage_counter_rejects_negative_values_and_odd_metric_names(session):
    for metric, value in (("requests", -1), ("Bad Metric", 1)):
        with pytest.raises(IntegrityError):
            await session.execute(text("INSERT INTO ops_usage_daily (day, metric, value) VALUES (current_date, :m, :v)"), {"m": metric, "v": value})
        await session.rollback()


async def test_audit_log_accepts_only_known_resources_and_actions(session):
    insert = "INSERT INTO kb_audit_log (resource, resource_key, action, actor_hash) VALUES (:r, 'k', :a, :h)"
    await session.execute(text(insert), {"r": "item_cache", "a": "revert", "h": "a" * 64})
    await session.commit()
    for resource, action in (("table", "update"), ("brand", "rename")):
        with pytest.raises(IntegrityError):
            await session.execute(text(insert), {"r": resource, "a": action, "h": "a" * 64})
        await session.rollback()
