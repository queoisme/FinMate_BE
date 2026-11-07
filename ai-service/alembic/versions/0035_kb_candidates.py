"""kb candidates

``agent_kb_candidates``: câu hỏi tri thức mà agent không trả lời được, CHỈ lưu mã băm SHA-256 và số lần gặp (không nội dung, không mã người dùng) để nhân sự nội dung biết kho còn thiếu gì.
Bảng mới, không đụng bảng nào khác. Bước lùi xóa bảng.

Revision ID: 0035
Revises: 0034
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql

revision: str = '0035'
down_revision: Union[str, Sequence[str], None] = '0034'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table('agent_kb_candidates',
    sa.Column('id', postgresql.UUID(as_uuid=True), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('question_hash', sa.String(length=64), nullable=False),
    sa.Column('count', sa.Integer(), server_default=sa.text('1'), nullable=False),
    sa.Column('first_seen', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('last_seen', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('status', sa.String(length=10), server_default=sa.text("'New'"), nullable=False),
    sa.CheckConstraint("question_hash ~ '^[0-9a-f]{64}$'", name='chk_agent_kb_candidates_hash'),
    sa.CheckConstraint("status IN ('New', 'Dismissed', 'Covered')", name='chk_agent_kb_candidates_status'),
    sa.CheckConstraint('count >= 1', name='chk_agent_kb_candidates_count'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('question_hash', name='uq_agent_kb_candidates_hash')
    )


def downgrade() -> None:
    op.drop_table('agent_kb_candidates')
