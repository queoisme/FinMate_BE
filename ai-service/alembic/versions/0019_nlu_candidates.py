"""nlu candidates

Thêm ``nlu_candidates``: câu chat chưa rõ (đã được người dùng đồng ý chia sẻ) chờ người duyệt gán nhãn trước khi vào ``nlu_examples``. CHỈ THÊM một bảng.

Revision ID: 0019
Revises: 0018
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '0019'
down_revision: Union[str, Sequence[str], None] = '0018'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table('nlu_candidates',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('user_id_hash', sa.String(length=64), nullable=False),
    sa.Column('text', sa.Text(), nullable=False),
    sa.Column('text_key', sa.String(length=400), nullable=False),
    sa.Column('predicted_intent', sa.String(length=32), nullable=True),
    sa.Column('predicted_confidence', sa.Float(), nullable=True),
    sa.Column('unclear_reason', sa.String(length=24), nullable=False),
    sa.Column('status', sa.String(length=16), server_default='pending', nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint("status IN ('pending', 'rejected')", name='chk_nlu_candidates_status'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('user_id_hash', 'text_key', name='uq_nlu_candidates_user_text')
    )
    op.create_index('idx_nlu_candidates_status_created', 'nlu_candidates', ['status', 'created_at'], unique=False)


def downgrade() -> None:
    op.drop_index('idx_nlu_candidates_status_created', table_name='nlu_candidates')
    op.drop_table('nlu_candidates')
