"""label events and item feedback

Giai đoạn 4 của ERD v5_1: ``ml_label_events`` (lịch sử nhãn, chỉ ghi thêm), ``ml_item_feedback`` (phiếu theo món) và hai view đồng thuận ``v_merchant_consensus``,
``v_item_consensus``. Nhãn hiện có của ``ml_labeled_samples`` được chép thành MỖI MẪU MỘT sự kiện đầu tiên (thời điểm = ``labeled_at``, nguồn suy từ ``labeled_by``;
``feedback_id`` để trống vì dữ liệu cũ không lưu mối liên kết đó). Trigger chặn UPDATE/DELETE trực tiếp trên ``ml_label_events``; view và trigger nằm ở ``app/db/ml_sql.py``.

Revision ID: 0024
Revises: 0023
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa

from app.db import ml_sql


# revision identifiers, used by Alembic.
revision: str = '0024'
down_revision: Union[str, Sequence[str], None] = '0023'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None

# labeled_by (người gắn nhãn) -> source của sự kiện đầu tiên.
BACKFILL = """
INSERT INTO ml_label_events (labeled_sample_id, category_slug, is_financial, source, created_at)
SELECT id, category_slug, is_financial,
       CASE labeled_by
            WHEN 'seed' THEN 'import' WHEN 'imported' THEN 'import'
            WHEN 'user_feedback' THEN 'feedback'
            WHEN 'human' THEN 'user' WHEN 'user' THEN 'user'
            WHEN 'gold' THEN 'gold'
            ELSE 'system' END,
       labeled_at
FROM ml_labeled_samples
"""


def upgrade() -> None:
    op.create_table('ml_label_events',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('labeled_sample_id', sa.UUID(), nullable=False),
    sa.Column('category_slug', sa.String(length=32), nullable=True),
    sa.Column('is_financial', sa.Boolean(), nullable=False),
    sa.Column('source', sa.String(length=16), nullable=False),
    sa.Column('feedback_id', sa.UUID(), nullable=True),
    sa.Column('actor_hash', sa.String(length=64), nullable=True),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint("source IN ('feedback', 'gold', 'import', 'user', 'system')", name='chk_ml_label_events_source'),
    sa.CheckConstraint('is_financial OR category_slug IS NULL', name='chk_ml_label_events_shape'),
    sa.CheckConstraint("actor_hash IS NULL OR actor_hash ~ '^[0-9a-f]{64}$'", name='chk_ml_label_events_actor_hash'),
    sa.ForeignKeyConstraint(['category_slug'], ['kb_categories.slug'], onupdate='CASCADE', ondelete='RESTRICT'),
    sa.ForeignKeyConstraint(['feedback_id'], ['ml_user_feedback.id'], ondelete='SET NULL'),
    sa.ForeignKeyConstraint(['labeled_sample_id'], ['ml_labeled_samples.id'], ondelete='CASCADE'),
    sa.PrimaryKeyConstraint('id')
    )
    op.create_index('idx_ml_label_events_sample_created', 'ml_label_events', ['labeled_sample_id', 'created_at'], unique=False)
    op.create_index('idx_ml_label_events_feedback', 'ml_label_events', ['feedback_id'], unique=False, postgresql_where=sa.text('feedback_id IS NOT NULL'))

    op.create_table('ml_item_feedback',
    sa.Column('item_key', sa.Text(), nullable=False),
    sa.Column('user_id_hash', sa.String(length=64), nullable=False),
    sa.Column('category_slug', sa.String(length=32), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint('char_length(item_key) BETWEEN 3 AND 60', name='chk_ml_item_feedback_key_len'),
    sa.CheckConstraint('item_key = lower(item_key)', name='chk_ml_item_feedback_key_lower'),
    sa.CheckConstraint("user_id_hash ~ '^[0-9a-f]{64}$'", name='chk_ml_item_feedback_user_hash'),
    sa.CheckConstraint("category_slug IN ('food', 'transport', 'shopping', 'education', 'housing', 'bills', 'entertainment', 'health', 'family', 'other')", name='chk_ml_item_feedback_slug'),
    sa.ForeignKeyConstraint(['category_slug'], ['kb_categories.slug'], onupdate='CASCADE', ondelete='RESTRICT'),
    sa.PrimaryKeyConstraint('item_key', 'user_id_hash')
    )
    op.create_index('idx_ml_item_feedback_item_category', 'ml_item_feedback', ['item_key', 'category_slug'], unique=False)
    op.create_index('idx_ml_training_samples_merchant_slug', 'ml_training_samples', ['merchant_key', 'final_slug'], unique=False, postgresql_where=sa.text('final_slug IS NOT NULL'))

    op.execute(BACKFILL)
    for statement in ml_sql.OBJECTS:  # trigger sau khi chép (chép chỉ INSERT nên thứ tự không bắt buộc), rồi hai view
        op.execute(statement)


def downgrade() -> None:
    for statement in ml_sql.DROP_OBJECTS:
        op.execute(statement)
    op.drop_index('idx_ml_training_samples_merchant_slug', table_name='ml_training_samples', postgresql_where=sa.text('final_slug IS NOT NULL'))
    op.drop_index('idx_ml_item_feedback_item_category', table_name='ml_item_feedback')
    op.drop_table('ml_item_feedback')
    op.drop_index('idx_ml_label_events_feedback', table_name='ml_label_events', postgresql_where=sa.text('feedback_id IS NOT NULL'))
    op.drop_index('idx_ml_label_events_sample_created', table_name='ml_label_events')
    op.drop_table('ml_label_events')
