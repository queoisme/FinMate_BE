"""bill tables

Thêm 10 bảng ``bill_*`` cho luồng quét hóa đơn: kiến thức nhận diện (thương hiệu, từ khóa, cửa hàng người dùng đã chốt),
dữ liệu huấn luyện đã lọc dữ liệu cá nhân, lịch sử huấn luyện, và hàng đợi bill lạ. CHỈ THÊM: không sửa hay xóa bảng nào có sẵn.

Vì sao không dùng lại ``raw_samples`` / ``labeled_samples``: hai bảng đó có CHECK riêng (11 slug của backend, nguồn mẫu cố định,
người gắn nhãn cố định), còn bill dùng 9 nhóm của bộ luật bill. Nới CHECK là sửa bảng của luồng thông báo ngân hàng, ép về 11 slug là
mất phân biệt Ăn uống / Siêu thị. Chi tiết ở ``app/db/models/bill.py``.

Hai view (``bill_unknown_merchants``, ``bill_merchant_votes``) là hàng đợi cho người duyệt thương hiệu, không có model ORM.
Dữ liệu cũ được nạp riêng bằng ``scripts/import_bill_data.py`` (migration chỉ tạo schema).

Revision ID: 0006
Revises: 0005
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql

from app.db.bill_views import MERCHANT_VOTES_VIEW, UNKNOWN_MERCHANTS_VIEW

# revision identifiers, used by Alembic.
revision: str = '0006'
down_revision: Union[str, Sequence[str], None] = '0005'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table('bill_brands',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('brand_key', sa.Text(), nullable=False),
    sa.Column('category_slug', sa.String(length=32), nullable=False),
    sa.Column('display_name', sa.Text(), nullable=False),
    sa.Column('whole_word', sa.Boolean(), server_default=sa.text('false'), nullable=False),
    sa.Column('is_active', sa.Boolean(), server_default=sa.text('true'), nullable=False),
    sa.Column('source', sa.String(length=16), server_default=sa.text("'seed'"), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('brand_key', name='uq_bill_brands_brand_key')
    )
    op.create_table('bill_keywords',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('kind', sa.String(length=16), nullable=False),
    sa.Column('category_slug', sa.String(length=32), nullable=False),
    sa.Column('keyword', sa.Text(), nullable=False),
    sa.Column('category_priority', sa.Integer(), server_default=sa.text('100'), nullable=False),
    sa.Column('is_active', sa.Boolean(), server_default=sa.text('true'), nullable=False),
    sa.Column('source', sa.String(length=16), server_default=sa.text("'seed'"), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint("kind IN ('merchant', 'product', 'chat')", name='chk_bill_keywords_kind'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('kind', 'category_slug', 'keyword', name='uq_bill_keyword')
    )
    op.create_table('bill_raw_samples',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('source', sa.String(length=32), nullable=False),
    sa.Column('package_name', sa.Text(), nullable=False),
    sa.Column('notification_title', sa.Text(), nullable=True),
    sa.Column('notification_body', sa.Text(), nullable=False),
    sa.Column('received_at', sa.DateTime(timezone=True), nullable=False),
    sa.Column('user_id_hash', sa.String(length=64), nullable=True),
    sa.Column('content_hash', sa.String(length=64), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint("source = 'bill_scan'", name='chk_bill_raw_samples_source'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('content_hash', name='uq_bill_raw_samples_content_hash')
    )
    op.create_index('idx_bill_raw_samples_created_at', 'bill_raw_samples', ['created_at'], unique=False)
    op.create_index('idx_bill_raw_samples_source', 'bill_raw_samples', ['source'], unique=False)
    op.create_table('bill_training_jobs',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('stage', sa.String(length=32), nullable=False),
    sa.Column('status', sa.String(length=16), nullable=False),
    sa.Column('params', postgresql.JSONB(astext_type=sa.Text()), nullable=True),
    sa.Column('sample_count', sa.Integer(), nullable=True),
    sa.Column('started_at', sa.DateTime(timezone=True), nullable=True),
    sa.Column('finished_at', sa.DateTime(timezone=True), nullable=True),
    sa.Column('error_message', sa.Text(), nullable=True),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.PrimaryKeyConstraint('id')
    )
    op.create_index('idx_bill_training_jobs_stage_created', 'bill_training_jobs', ['stage', 'created_at'], unique=False)
    op.create_table('bill_unknown_scans',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('user_id_hash', sa.String(length=64), nullable=False),
    sa.Column('merchant_read', sa.Text(), nullable=True),
    sa.Column('merchant_key', sa.Text(), server_default=sa.text("''"), nullable=False),
    sa.Column('predicted_category', sa.String(length=32), nullable=True),
    sa.Column('predicted_source', sa.String(length=16), nullable=True),
    sa.Column('confidence', sa.Float(), nullable=True),
    sa.Column('total_amount', sa.BigInteger(), nullable=True),
    sa.Column('lines', sa.Text(), nullable=False),
    sa.Column('content_hash', sa.String(length=64), nullable=False),
    sa.Column('seen_count', sa.Integer(), server_default=sa.text('1'), nullable=False),
    sa.Column('status', sa.String(length=16), server_default=sa.text("'new'"), nullable=False),
    sa.Column('first_seen_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('last_seen_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint("status IN ('new', 'labeled', 'ignored')", name='chk_bill_unknown_scans_status'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('content_hash', name='uq_bill_unknown_scans_content_hash')
    )
    op.create_index('idx_bill_unknown_scans_status_merchant', 'bill_unknown_scans', ['status', 'merchant_key'], unique=False)
    op.create_table('bill_user_merchant_category',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('user_id_hash', sa.String(length=64), nullable=False),
    sa.Column('merchant_key', sa.Text(), nullable=False),
    sa.Column('display_name', sa.Text(), nullable=True),
    sa.Column('category_slug', sa.String(length=32), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('user_id_hash', 'merchant_key', name='uq_bill_user_merchant')
    )
    op.create_index('idx_bill_user_merchant_vote', 'bill_user_merchant_category', ['merchant_key', 'category_slug'], unique=False)
    op.create_table('bill_labeled_samples',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('raw_sample_id', sa.UUID(), nullable=False),
    sa.Column('is_financial', sa.Boolean(), nullable=False),
    sa.Column('transaction_type', sa.String(length=16), nullable=True),
    sa.Column('amount_cents', sa.BigInteger(), nullable=True),
    sa.Column('merchant_name', sa.Text(), nullable=True),
    sa.Column('category_slug', sa.String(length=32), nullable=True),
    sa.Column('labeled_by', sa.String(length=32), nullable=False),
    sa.Column('labeled_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('is_gold', sa.Boolean(), server_default=sa.text('false'), nullable=False),
    sa.CheckConstraint("category_slug IS NULL OR category_slug IN ('dining', 'transport', 'shopping', 'groceries', 'housing', 'utilities', 'healthcare', 'entertainment', 'other')", name='chk_bill_labeled_samples_category'),
    sa.CheckConstraint("labeled_by IN ('user', 'imported', 'gold', 'synthetic_ocr')", name='chk_bill_labeled_samples_labeled_by'),
    sa.ForeignKeyConstraint(['raw_sample_id'], ['bill_raw_samples.id'], ondelete='CASCADE'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('raw_sample_id', name='uq_bill_labeled_samples_raw_sample')
    )
    op.create_index('idx_bill_labeled_samples_category_slug', 'bill_labeled_samples', ['category_slug'], unique=False)
    op.create_table('bill_model_versions',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('stage', sa.String(length=32), nullable=False),
    sa.Column('version', sa.String(length=32), nullable=False),
    sa.Column('artifact_path', sa.Text(), nullable=False),
    sa.Column('status', sa.String(length=16), nullable=False),
    sa.Column('metrics', postgresql.JSONB(astext_type=sa.Text()), nullable=True),
    sa.Column('training_job_id', sa.UUID(), nullable=True),
    sa.Column('trained_at', sa.DateTime(timezone=True), nullable=True),
    sa.Column('notes', sa.Text(), nullable=True),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.ForeignKeyConstraint(['training_job_id'], ['bill_training_jobs.id'], ondelete='SET NULL'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('stage', 'version', name='uq_bill_model_versions_stage_version')
    )
    op.create_table('bill_evaluation_runs',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('model_version_id', sa.UUID(), nullable=False),
    sa.Column('split', sa.String(length=16), nullable=False),
    sa.Column('sample_count', sa.Integer(), nullable=False),
    sa.Column('accuracy', sa.Float(), nullable=False),
    sa.Column('macro_f1', sa.Float(), nullable=False),
    sa.Column('metrics', postgresql.JSONB(astext_type=sa.Text()), nullable=True),
    sa.Column('notes', sa.Text(), nullable=True),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.ForeignKeyConstraint(['model_version_id'], ['bill_model_versions.id'], ondelete='CASCADE'),
    sa.PrimaryKeyConstraint('id')
    )
    op.create_index('idx_bill_evaluation_runs_model_created', 'bill_evaluation_runs', ['model_version_id', 'created_at'], unique=False)
    op.create_table('bill_evaluation_category_metrics',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('evaluation_run_id', sa.UUID(), nullable=False),
    sa.Column('label', sa.String(length=64), nullable=False),
    sa.Column('precision', sa.Float(), nullable=False),
    sa.Column('recall', sa.Float(), nullable=False),
    sa.Column('f1', sa.Float(), nullable=False),
    sa.Column('support', sa.Integer(), nullable=False),
    sa.ForeignKeyConstraint(['evaluation_run_id'], ['bill_evaluation_runs.id'], ondelete='CASCADE'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('evaluation_run_id', 'label', name='uq_bill_eval_category_metrics')
    )

    op.execute(UNKNOWN_MERCHANTS_VIEW)
    op.execute(MERCHANT_VOTES_VIEW)


def downgrade() -> None:
    op.execute("DROP VIEW IF EXISTS bill_merchant_votes")
    op.execute("DROP VIEW IF EXISTS bill_unknown_merchants")
    op.drop_table('bill_evaluation_category_metrics')
    op.drop_index('idx_bill_evaluation_runs_model_created', table_name='bill_evaluation_runs')
    op.drop_table('bill_evaluation_runs')
    op.drop_table('bill_model_versions')
    op.drop_index('idx_bill_labeled_samples_category_slug', table_name='bill_labeled_samples')
    op.drop_table('bill_labeled_samples')
    op.drop_index('idx_bill_user_merchant_vote', table_name='bill_user_merchant_category')
    op.drop_table('bill_user_merchant_category')
    op.drop_index('idx_bill_unknown_scans_status_merchant', table_name='bill_unknown_scans')
    op.drop_table('bill_unknown_scans')
    op.drop_index('idx_bill_training_jobs_stage_created', table_name='bill_training_jobs')
    op.drop_table('bill_training_jobs')
    op.drop_index('idx_bill_raw_samples_source', table_name='bill_raw_samples')
    op.drop_index('idx_bill_raw_samples_created_at', table_name='bill_raw_samples')
    op.drop_table('bill_raw_samples')
    op.drop_table('bill_keywords')
    op.drop_table('bill_brands')
