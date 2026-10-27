"""erd v5_1 phase 1

Giai đoạn 1 của ERD v5_1, CHƯA gộp bảng: đổi tên 14 bảng sang tiền tố kb_/ml_/ops_, thêm cột và ràng buộc.
Không đổi tên chỉ mục/ràng buộc cũ (Postgres giữ nguyên khi đổi tên bảng). Mọi CHECK mới được thêm NOT VALID rồi VALIDATE ngay: nếu dữ liệu cũ vi phạm,
migration dừng và nói rõ tên ràng buộc (cả migration được hoàn tác vì DDL của Postgres nằm trong giao dịch).

Revision ID: 0021
Revises: 0020
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql


# revision identifiers, used by Alembic.
revision: str = '0021'
down_revision: Union[str, Sequence[str], None] = '0020'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None

RENAMES = [
    ('bill_brands', 'kb_brands'),
    ('bill_keywords', 'kb_keywords'),
    ('bill_item_cache', 'kb_item_categories'),
    ('bill_ai_instructions', 'kb_ai_instructions'),
    ('provider_patterns', 'kb_provider_patterns'),
    ('bill_rule_audit', 'kb_audit_log'),
    ('bill_user_merchant_choice', 'user_merchant_memory'),
    ('pipeline_requests', 'ops_analyze_log'),
    ('bill_classify_stats', 'ops_usage_daily'),
    ('user_feedback', 'ml_user_feedback'),
    ('raw_samples', 'ml_raw_samples'),
    ('feedback_batch_jobs', 'ml_feedback_batches'),
    ('sample_splits', 'ml_sample_splits'),
    ('bill_training_samples', 'ml_training_samples'),
]

# (bảng sau khi đổi tên, tên ràng buộc, điều kiện) — giống hệt CheckConstraint trong model.
CHECKS = [
    ('ops_analyze_log', 'chk_ops_analyze_log_fallback_reason', "categorizer_fallback_reason IS NULL OR categorizer_fallback_reason IN ('timeout', 'bad_json', 'out_of_taxonomy')"),
    ('ops_analyze_log', 'chk_ops_analyze_log_classifier_conf', 'classifier_confidence IS NULL OR classifier_confidence BETWEEN 0 AND 1'),
    ('ops_analyze_log', 'chk_ops_analyze_log_extraction_conf', 'extraction_confidence IS NULL OR extraction_confidence BETWEEN 0 AND 1'),
    ('ops_analyze_log', 'chk_ops_analyze_log_categorization_conf', 'categorization_confidence IS NULL OR categorization_confidence BETWEEN 0 AND 1'),
    ('ops_analyze_log', 'chk_ops_analyze_log_amount', 'amount_cents IS NULL OR amount_cents >= 0'),
    ('ops_analyze_log', 'chk_ops_analyze_log_processing_ms', 'processing_ms IS NULL OR processing_ms >= 0'),
    ('ops_analyze_log', 'chk_ops_analyze_log_extra_fields', "extra_fields IS NULL OR jsonb_typeof(extra_fields) = 'object'"),
    ('ops_usage_daily', 'chk_ops_usage_daily_value', 'value >= 0'),
    ('ops_usage_daily', 'chk_ops_usage_daily_metric', "metric ~ '^[a-z0-9_]+$'"),
    ('kb_brands', 'chk_kb_brands_key_lower', 'brand_key = lower(brand_key)'),
    ('kb_keywords', 'chk_kb_keywords_priority', 'category_priority >= 0'),
    ('kb_item_categories', 'chk_kb_item_categories_hits', 'hits >= 1'),
    ('kb_item_categories', 'chk_kb_item_categories_source', "source IN ('ai', 'admin', 'consensus')"),
    ('kb_item_categories', 'chk_kb_item_categories_key_len', 'char_length(item_key) BETWEEN 3 AND 60'),
    ('kb_ai_instructions', 'chk_kb_ai_instructions_text', 'char_length(instruction_text) > 0'),
    ('kb_provider_patterns', 'chk_kb_provider_patterns_priority', 'priority >= 0'),
    ('kb_audit_log', 'chk_kb_audit_log_resource', "resource IN ('brand', 'keyword', 'item_cache', 'instruction', 'provider_format')"),
    ('kb_audit_log', 'chk_kb_audit_log_action', "action IN ('create', 'update', 'activate', 'deactivate', 'delete', 'revert')"),
    ('user_merchant_memory', 'chk_user_merchant_memory_key_lower', 'merchant_key = lower(merchant_key)'),
    ('ml_feedback_batches', 'chk_ml_feedback_batches_counts', 'feedback_count >= 0 AND created_labeled_samples >= 0'),
    ('ml_feedback_batches', 'chk_ml_feedback_batches_times', 'finished_at IS NULL OR finished_at >= started_at'),
    ('ml_training_samples', 'chk_ml_training_samples_unit', "sample_unit IN ('bill', 'item')"),
    ('nlu_examples', 'chk_nlu_examples_text_len', 'char_length(text) <= 200'),
    ('config_documents', 'chk_config_documents_version', 'version >= 1'),
    ('config_documents', 'chk_config_documents_content_object', "jsonb_typeof(content) = 'object'"),
]


def upgrade() -> None:
    for old, new in RENAMES:
        op.rename_table(old, new)
    op.alter_column('ml_user_feedback', 'batch_job_id', new_column_name='batch_id')

    op.add_column('ops_analyze_log', sa.Column('categorizer_fallback_reason', sa.String(length=32), nullable=True))
    op.add_column('ops_analyze_log', sa.Column('pattern_id', sa.UUID(), nullable=True))
    op.add_column('ops_analyze_log', sa.Column('config_versions', postgresql.JSONB(astext_type=sa.Text()), nullable=True))
    op.add_column('ops_analyze_log', sa.Column('extra_fields', postgresql.JSONB(astext_type=sa.Text()), nullable=True))
    op.add_column('kb_brands', sa.Column('mixed', sa.Boolean(), server_default=sa.text('false'), nullable=False))
    op.add_column('kb_item_categories', sa.Column('source', sa.String(length=16), server_default=sa.text("'ai'"), nullable=False))
    op.add_column('kb_item_categories', sa.Column('ai_model', sa.String(length=64), nullable=True))
    op.add_column('ml_training_samples', sa.Column('sample_unit', sa.String(length=8), server_default=sa.text("'bill'"), nullable=False))

    # Con trỏ treo (dòng cha đã bị xóa) phải về NULL trước khi có khóa ngoại, nếu không việc thêm khóa ngoại thất bại.
    op.execute(
        "UPDATE ops_analyze_log AS child SET duplicate_of_request_id = NULL "
        "WHERE duplicate_of_request_id IS NOT NULL "
        "AND NOT EXISTS (SELECT 1 FROM ops_analyze_log AS parent WHERE parent.id = child.duplicate_of_request_id)"
    )
    op.create_foreign_key(
        'ops_analyze_log_duplicate_of_request_id_fkey', 'ops_analyze_log', 'ops_analyze_log',
        ['duplicate_of_request_id'], ['id'], ondelete='SET NULL',
    )

    for table, name, expr in CHECKS:
        op.execute(f"ALTER TABLE {table} ADD CONSTRAINT {name} CHECK ({expr}) NOT VALID")
        op.execute(f"ALTER TABLE {table} VALIDATE CONSTRAINT {name}")

    op.create_index('idx_ops_analyze_log_duplicate_of', 'ops_analyze_log', ['duplicate_of_request_id'], unique=False, postgresql_where=sa.text('duplicate_of_request_id IS NOT NULL'))
    op.create_index('idx_kb_brands_category', 'kb_brands', ['category_slug'], unique=False)
    op.create_index('idx_kb_keywords_active_load', 'kb_keywords', ['category_priority', 'category_slug', 'keyword'], unique=False, postgresql_where=sa.text('is_active'))
    op.create_index('idx_kb_item_categories_hits', 'kb_item_categories', ['hits'], unique=False)


def downgrade() -> None:
    op.drop_index('idx_kb_item_categories_hits', table_name='kb_item_categories')
    op.drop_index('idx_kb_keywords_active_load', table_name='kb_keywords', postgresql_where=sa.text('is_active'))
    op.drop_index('idx_kb_brands_category', table_name='kb_brands')
    op.drop_index('idx_ops_analyze_log_duplicate_of', table_name='ops_analyze_log', postgresql_where=sa.text('duplicate_of_request_id IS NOT NULL'))

    for table, name, _expr in reversed(CHECKS):
        op.execute(f"ALTER TABLE {table} DROP CONSTRAINT {name}")

    op.drop_constraint('ops_analyze_log_duplicate_of_request_id_fkey', 'ops_analyze_log', type_='foreignkey')

    op.drop_column('ml_training_samples', 'sample_unit')
    op.drop_column('kb_item_categories', 'ai_model')
    op.drop_column('kb_item_categories', 'source')
    op.drop_column('kb_brands', 'mixed')
    op.drop_column('ops_analyze_log', 'extra_fields')
    op.drop_column('ops_analyze_log', 'config_versions')
    op.drop_column('ops_analyze_log', 'pattern_id')
    op.drop_column('ops_analyze_log', 'categorizer_fallback_reason')

    op.alter_column('ml_user_feedback', 'batch_id', new_column_name='batch_job_id')
    for old, new in reversed(RENAMES):
        op.rename_table(new, old)
