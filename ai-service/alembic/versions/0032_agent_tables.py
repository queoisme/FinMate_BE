"""agent tables

Thêm ba bảng nền của AI Financial Agent (xem ``app/db/models/agent.py``): ``agent_runs`` (vết từng bước của một lượt hỏi), ``agent_refusal_templates`` (câu từ chối mẫu)
và ``agent_user_memory`` (sở thích dài hạn, ≤200 ký tự). Bảng mới, không đụng bảng nào khác. Mọi ``user_id_hash`` bị CHECK ép đúng dạng SHA-256 hex.

Extension ``vector`` (pgvector) chưa bật ở đây: nó thuộc migration 0033 cùng bảng tri thức, để bước này chạy được kể cả khi image Postgres chưa có pgvector.

Bước lùi chỉ xóa ba bảng: vết và bộ nhớ không phải dữ liệu gốc của ai (có thể dựng lại bằng cách hỏi lại).

Revision ID: 0032
Revises: 0031
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql


# revision identifiers, used by Alembic.
revision: str = '0032'
down_revision: Union[str, Sequence[str], None] = '0031'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


HASH = "~ '^[0-9a-f]{64}$'"
STEPS = "('context', 'gate', 'plan', 'execute', 'compose', 'verify', 'respond')"


def upgrade() -> None:
    op.create_table('agent_runs',
    sa.Column('id', postgresql.UUID(as_uuid=True), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('trace_id', postgresql.UUID(as_uuid=True), nullable=False),
    sa.Column('user_id_hash', sa.String(length=64), nullable=False),
    sa.Column('step', sa.String(length=16), nullable=False),
    sa.Column('tool', sa.String(length=48), nullable=True),
    sa.Column('latency_ms', sa.Integer(), nullable=False),
    sa.Column('input_tokens', sa.Integer(), server_default=sa.text('0'), nullable=False),
    sa.Column('output_tokens', sa.Integer(), server_default=sa.text('0'), nullable=False),
    sa.Column('verdict', sa.String(length=24), nullable=True),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint(f"user_id_hash {HASH}", name='chk_agent_runs_user_hash'),
    sa.CheckConstraint(f"step IN {STEPS}", name='chk_agent_runs_step'),
    sa.CheckConstraint('latency_ms >= 0 AND input_tokens >= 0 AND output_tokens >= 0', name='chk_agent_runs_non_negative'),
    sa.PrimaryKeyConstraint('id', name=op.f('pk_agent_runs')),
    )
    op.create_index('ix_agent_runs_trace', 'agent_runs', ['trace_id'], unique=False)
    op.create_index('ix_agent_runs_user_created', 'agent_runs', ['user_id_hash', 'created_at'], unique=False)
    op.create_table('agent_refusal_templates',
    sa.Column('code', sa.String(length=32), nullable=False),
    sa.Column('text', sa.Text(), nullable=False),
    sa.Column('is_active', sa.Boolean(), server_default=sa.text('true'), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.PrimaryKeyConstraint('code', name=op.f('pk_agent_refusal_templates')),
    )
    op.create_table('agent_user_memory',
    sa.Column('user_id_hash', sa.String(length=64), nullable=False),
    sa.Column('key', sa.String(length=48), nullable=False),
    sa.Column('value', sa.String(length=200), nullable=False),
    sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint(f"user_id_hash {HASH}", name='chk_agent_user_memory_user_hash'),
    sa.PrimaryKeyConstraint('user_id_hash', 'key', name=op.f('pk_agent_user_memory')),
    )


def downgrade() -> None:
    op.drop_table('agent_user_memory')
    op.drop_table('agent_refusal_templates')
    op.drop_index('ix_agent_runs_user_created', table_name='agent_runs')
    op.drop_index('ix_agent_runs_trace', table_name='agent_runs')
    op.drop_table('agent_runs')
