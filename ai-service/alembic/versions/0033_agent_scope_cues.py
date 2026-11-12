"""agent scope cues

Thêm ``agent_scope_cues``: cụm từ của cổng phạm vi AI Financial Agent (chủ đề cấm và cụm báo hiệu câu hỏi tài chính), lưu đã bỏ dấu, chữ thường, để admin sửa mà không cần đổi code.
Bảng mới, không đụng bảng nào khác. Bản mặc định (``app/data/agent_scope.json``) được gieo lúc khởi động, chỉ thêm, DB thắng.

Bước lùi chỉ xóa bảng: cụm từ có thể gieo lại từ tệp mặc định (các cụm admin đã thêm riêng sẽ mất).

Revision ID: 0033
Revises: 0032
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql


# revision identifiers, used by Alembic.
revision: str = '0033'
down_revision: Union[str, Sequence[str], None] = '0032'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table('agent_scope_cues',
    sa.Column('id', postgresql.UUID(as_uuid=True), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('kind', sa.String(length=12), nullable=False),
    sa.Column('topic', sa.String(length=32), nullable=False),
    sa.Column('phrase', sa.String(length=80), nullable=False),
    sa.Column('is_active', sa.Boolean(), server_default=sa.text('true'), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint("kind IN ('forbidden', 'in_scope')", name='chk_agent_scope_cues_kind'),
    sa.CheckConstraint('length(phrase) > 0 AND phrase = lower(btrim(phrase))', name='chk_agent_scope_cues_phrase'),
    sa.PrimaryKeyConstraint('id', name=op.f('pk_agent_scope_cues')),
    sa.UniqueConstraint('kind', 'topic', 'phrase', name='uq_agent_scope_cues_kind_topic_phrase'),
    )


def downgrade() -> None:
    op.drop_table('agent_scope_cues')
