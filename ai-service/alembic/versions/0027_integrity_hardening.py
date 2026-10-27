"""integrity hardening

Phần ràng buộc còn lại của ERD v5_1 (nhóm "chưa làm" của giai đoạn 1-5):
  - CHECK mã băm SHA-256 viết thường 64 ký tự cho mọi cột ``*_hash``/``created_by`` còn thiếu (``nlu_model_versions.dataset_hash`` cho thêm giá trị ``'unknown'`` mà
    sổ phiên bản NLU cũ dùng). Mã băm là cách DUY NHẤT AI Service biết một người dùng, nên một mã sai dạng (id thật, chuỗi rỗng) phải bị DB từ chối ngay.
    ``ml_raw_samples.user_id_hash`` của ocr-service từng mặc định là chuỗi ``local``: băm lại các dòng đó trước khi kiểm.
  - Trigger chỉ-ghi-thêm cho ``kb_audit_log`` (``app/db/audit_sql.py``).
  - Khóa ngoại ``ops_analyze_log.pattern_id`` -> ``kb_provider_patterns`` ON DELETE SET NULL (+ chỉ mục từng phần), dọn con trỏ treo trước.
  - F1-04: ``NOT (is_sensitive AND maps_to IS NULL)``: trường nhạy cảm không bao giờ được là trường phụ (trường phụ đi vào ``ops_analyze_log.extra_fields``).
  - F1-09: bỏ CHECK ``transaction_type`` và ``extraction_method`` của ``ops_analyze_log``; ERD chỉ giữ tập đóng cho ``pipeline_result`` và ``classifier_label``, vì
    định dạng nhà cung cấp có thể mang loại giao dịch hay cách bóc mới.

Mọi CHECK thêm NOT VALID rồi VALIDATE: dữ liệu cũ vi phạm thì migration dừng và nêu tên ràng buộc.

Revision ID: 0027
Revises: 0026
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa

from app.db import audit_sql


# revision identifiers, used by Alembic.
revision: str = '0027'
down_revision: Union[str, Sequence[str], None] = '0026'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None

HEX = "~ '^[0-9a-f]{64}$'"
CHECKS = [
    ('user_merchant_memory', 'chk_user_merchant_memory_user_hash', f"user_id_hash {HEX}"),
    ('ml_training_samples', 'chk_ml_training_samples_user_hash', f"user_id_hash {HEX}"),
    ('kb_audit_log', 'chk_kb_audit_log_actor_hash', f"actor_hash {HEX}"),
    ('config_documents', 'chk_config_documents_created_by', f"created_by {HEX}"),
    ('nlu_candidates', 'chk_nlu_candidates_user_hash', f"user_id_hash {HEX}"),
    ('nlu_model_versions', 'chk_nlu_model_versions_dataset_hash', f"dataset_hash = 'unknown' OR dataset_hash {HEX}"),
    ('ops_analyze_log', 'chk_ops_analyze_log_user_hash', f"user_id_hash {HEX}"),
    ('ml_raw_samples', 'chk_ml_raw_samples_user_hash', f"user_id_hash IS NULL OR user_id_hash {HEX}"),
    ('ml_raw_samples', 'chk_ml_raw_samples_content_hash', f"content_hash {HEX}"),
    ('ml_user_feedback', 'chk_ml_user_feedback_user_hash', f"user_id_hash {HEX}"),
    ('ml_user_feedback', 'chk_ml_user_feedback_txn_hash', f"backend_transaction_id_hash {HEX}"),
    ('kb_provider_format_fields', 'chk_kb_provider_format_fields_sensitive_mapped', "NOT (is_sensitive AND maps_to IS NULL)"),
]
OLD_TRANSACTION_TYPE = "transaction_type IS NULL OR transaction_type IN ('debit', 'credit')"
OLD_EXTRACTION_METHOD = "extraction_method IS NULL OR extraction_method IN ('rule', 'model')"


def _check(table: str, name: str, expr: str) -> None:
    op.execute(f"ALTER TABLE {table} ADD CONSTRAINT {name} CHECK ({expr}) NOT VALID")
    op.execute(f"ALTER TABLE {table} VALIDATE CONSTRAINT {name}")


def upgrade() -> None:
    # Dòng ocr-service ghi với mã người dùng mặc định "local" (chưa phải mã băm): băm lại thay vì làm migration dừng vì dữ liệu dev.
    op.execute(
        "UPDATE ml_raw_samples SET user_id_hash = encode(sha256(convert_to(user_id_hash, 'UTF8')), 'hex') "
        f"WHERE user_id_hash IS NOT NULL AND user_id_hash !{HEX}"
    )
    for table, name, expr in CHECKS:
        _check(table, name, expr)

    for statement in audit_sql.OBJECTS:
        op.execute(statement)

    # Con trỏ treo (mẫu pattern đã bị xóa) thành NULL trước khi thêm khóa ngoại.
    op.execute(
        "UPDATE ops_analyze_log SET pattern_id = NULL "
        "WHERE pattern_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM kb_provider_patterns p WHERE p.id = ops_analyze_log.pattern_id)"
    )
    op.create_foreign_key(
        'fk_ops_analyze_log_pattern', 'ops_analyze_log', 'kb_provider_patterns', ['pattern_id'], ['id'], ondelete='SET NULL'
    )
    op.create_index('idx_ops_analyze_log_pattern', 'ops_analyze_log', ['pattern_id'], unique=False, postgresql_where=sa.text('pattern_id IS NOT NULL'))

    op.drop_constraint('chk_pipeline_requests_transaction_type', 'ops_analyze_log', type_='check')
    op.drop_constraint('chk_pipeline_requests_extraction_method', 'ops_analyze_log', type_='check')


def downgrade() -> None:
    _check('ops_analyze_log', 'chk_pipeline_requests_transaction_type', OLD_TRANSACTION_TYPE)
    _check('ops_analyze_log', 'chk_pipeline_requests_extraction_method', OLD_EXTRACTION_METHOD)

    op.drop_index('idx_ops_analyze_log_pattern', table_name='ops_analyze_log', postgresql_where=sa.text('pattern_id IS NOT NULL'))
    op.drop_constraint('fk_ops_analyze_log_pattern', 'ops_analyze_log', type_='foreignkey')

    for statement in audit_sql.DROP_OBJECTS:
        op.execute(statement)

    for table, name, _ in reversed(CHECKS):
        op.drop_constraint(name, table, type_='check')
