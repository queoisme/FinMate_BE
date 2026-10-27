"""Migration 0027 trên Postgres thật: CHECK mã băm hex 64, nhật ký quản trị chỉ-ghi-thêm, khóa ngoại ``pattern_id``, F1-04 và F1-09."""

import uuid

import pytest
from sqlalchemy import text
from sqlalchemy.exc import DBAPIError

GOOD = "a" * 64

# (cột, câu INSERT với :h là mã băm cần kiểm). Mỗi câu chỉ có MỘT cột mã băm là :h, các cột còn lại hợp lệ.
HASH_INSERTS = {
    "user_merchant_memory.user_id_hash": "INSERT INTO user_merchant_memory (user_id_hash, merchant_key, category_slug) VALUES (:h, 'k', 'food')",
    "ml_training_samples.user_id_hash": "INSERT INTO ml_training_samples (scan_id, user_id_hash, lines) VALUES (gen_random_uuid(), :h, 'x')",
    "kb_audit_log.actor_hash": "INSERT INTO kb_audit_log (resource, resource_key, action, actor_hash) VALUES ('brand', 'k', 'create', :h)",
    "config_documents.created_by": "INSERT INTO config_documents (name, version, status, content, created_by) VALUES ('thresholds', 1, 'draft', '{}', :h)",
    "nlu_candidates.user_id_hash": "INSERT INTO nlu_candidates (user_id_hash, text, text_key, unclear_reason) VALUES (:h, 't', 't', 'low_confidence')",
    "nlu_model_versions.dataset_hash": "INSERT INTO nlu_model_versions (version, artifact_path, status, dataset_hash) VALUES ('v1', 'p', 'candidate', :h)",
    "ops_analyze_log.user_id_hash": "INSERT INTO ops_analyze_log (backend_request_id, user_id_hash, package_name, received_at, pipeline_result) VALUES (gen_random_uuid(), :h, 'p', now(), 'financial')",
    "ml_raw_samples.user_id_hash": "INSERT INTO ml_raw_samples (source, package_name, notification_body, received_at, user_id_hash, content_hash) VALUES ('notification', 'p', 'x', now(), :h, repeat('c', 64))",
    "ml_raw_samples.content_hash": "INSERT INTO ml_raw_samples (source, package_name, notification_body, received_at, content_hash) VALUES ('notification', 'p', 'x', now(), :h)",
    "ml_user_feedback.user_id_hash": "INSERT INTO ml_user_feedback (backend_transaction_id_hash, user_id_hash, package_name, feedback_type) VALUES (repeat('b', 64), :h, 'p', 'category_correction')",
    "ml_user_feedback.backend_transaction_id_hash": "INSERT INTO ml_user_feedback (backend_transaction_id_hash, user_id_hash, package_name, feedback_type) VALUES (:h, repeat('b', 64), 'p', 'category_correction')",
}


async def rejected(session, sql: str, **params) -> bool:
    try:
        await session.execute(text(sql), params)
        await session.commit()
        return False
    except DBAPIError:
        await session.rollback()
        return True


@pytest.mark.parametrize("column", sorted(HASH_INSERTS))
async def test_hash_columns_accept_only_lowercase_sha256_hex(session, column):
    sql = HASH_INSERTS[column]
    for bad in ("", "short", "A" * 64, "g" * 64, "a" * 63, "a" * 65, "user-123"):
        assert await rejected(session, sql, h=bad), f"{column} nhận {bad!r}"
    assert not await rejected(session, sql, h=GOOD)


async def test_the_nlu_dataset_hash_also_accepts_unknown(session):
    sql = HASH_INSERTS["nlu_model_versions.dataset_hash"]
    assert not await rejected(session, sql, h="unknown")


async def test_a_raw_sample_may_have_no_user(session):
    assert not await rejected(
        session, "INSERT INTO ml_raw_samples (source, package_name, notification_body, received_at, user_id_hash, content_hash) "
                 "VALUES ('notification', 'p', 'x', now(), NULL, repeat('d', 64))")


async def test_the_audit_log_can_only_be_appended_to(session):
    insert = HASH_INSERTS["kb_audit_log.actor_hash"]
    assert not await rejected(session, insert, h=GOOD)

    assert await rejected(session, "UPDATE kb_audit_log SET reason = 'sửa lén'")
    assert await rejected(session, "DELETE FROM kb_audit_log")
    assert (await session.execute(text("SELECT count(*) FROM kb_audit_log"))).scalar_one() == 1


async def test_audit_rows_written_by_the_application_still_work(session):
    from app.db.models.bill import BillRuleAudit

    session.add(BillRuleAudit(resource="brand", resource_key="highlands", action="create", actor_hash=GOOD))
    session.add(BillRuleAudit(resource="brand", resource_key="highlands", action="revert", actor_hash=GOOD))  # hoàn tác là dòng mới
    await session.commit()
    assert (await session.execute(text("SELECT count(*) FROM kb_audit_log"))).scalar_one() == 2


async def test_pattern_id_must_point_at_a_real_pattern_and_is_cleared_when_it_goes(seeded_session):
    session = seeded_session
    pattern_id = (await session.execute(text("SELECT id FROM kb_provider_patterns LIMIT 1"))).scalar_one()
    log = ("INSERT INTO ops_analyze_log (id, backend_request_id, user_id_hash, package_name, received_at, pipeline_result, pattern_id) "
           "VALUES (:id, gen_random_uuid(), repeat('a', 64), 'p', now(), 'financial', :p)")

    assert await rejected(session, log, id=uuid.uuid4(), p=uuid.uuid4())  # con trỏ vào pattern không có
    row = uuid.uuid4()
    assert not await rejected(session, log, id=row, p=pattern_id)

    await session.execute(text("DELETE FROM kb_provider_patterns WHERE id = :p"), {"p": pattern_id})
    await session.commit()
    assert (await session.execute(text("SELECT pattern_id FROM ops_analyze_log WHERE id = :id"), {"id": row})).scalar_one() is None  # lịch sử còn, con trỏ về NULL


async def test_transaction_type_and_extraction_method_are_open_sets(session):
    insert = ("INSERT INTO ops_analyze_log (backend_request_id, user_id_hash, package_name, received_at, pipeline_result, transaction_type, extraction_method) "
              "VALUES (gen_random_uuid(), repeat('a', 64), 'p', now(), 'financial', :t, :m)")
    assert not await rejected(session, insert, t="transfer", m="format_v2")  # định dạng nhà cung cấp có thể mang giá trị mới
    # tập đóng còn lại của ERD vẫn đóng
    assert await rejected(session, insert.replace("'financial'", "'bogus'"), t="debit", m="rule")


async def test_a_sensitive_field_must_map_to_a_contract_field(seeded_session):
    session = seeded_session
    format_id = (await session.execute(text("SELECT id FROM kb_provider_formats LIMIT 1"))).scalar_one()
    field = ("INSERT INTO kb_provider_format_fields (format_id, field_key, maps_to, data_type, source_kind, source_ref, is_sensitive) "
             "VALUES (:f, :k, :m, 'text', 'regex_group', 'x', :s)")

    assert await rejected(session, field, f=format_id, k="account", m=None, s=True)  # trường nhạy cảm không được thành trường phụ
    assert not await rejected(session, field, f=format_id, k="ref_code", m=None, s=False)
