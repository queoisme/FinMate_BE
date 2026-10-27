"""nlu model versions

Thêm ``nlu_model_versions``: sổ đăng ký model Rasa NLU (bản dữ liệu đã học, số đo thô và sau luật, bản đang chạy). CHỈ THÊM một bảng.

Revision ID: 0018
Revises: 0017
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql


# revision identifiers, used by Alembic.
revision: str = '0018'
down_revision: Union[str, Sequence[str], None] = '0017'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table('nlu_model_versions',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('version', sa.String(length=64), nullable=False),
    sa.Column('artifact_path', sa.Text(), nullable=False),
    sa.Column('status', sa.String(length=16), nullable=False),
    sa.Column('dataset_hash', sa.String(length=64), nullable=False),
    sa.Column('dataset_counts', postgresql.JSONB(astext_type=sa.Text()), nullable=True),
    sa.Column('eval_raw', postgresql.JSONB(astext_type=sa.Text()), nullable=True),
    sa.Column('eval_final', postgresql.JSONB(astext_type=sa.Text()), nullable=True),
    sa.Column('notes', sa.Text(), nullable=True),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('activated_at', sa.DateTime(timezone=True), nullable=True),
    sa.CheckConstraint("status IN ('candidate', 'active', 'retired', 'rejected')", name='chk_nlu_model_versions_status'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('version', name='uq_nlu_model_versions_version')
    )
    op.create_index('uq_nlu_model_versions_active', 'nlu_model_versions', ['status'], unique=True, postgresql_where=sa.text("status = 'active'"))


def downgrade() -> None:
    op.drop_index('uq_nlu_model_versions_active', table_name='nlu_model_versions', postgresql_where=sa.text("status = 'active'"))
    op.drop_table('nlu_model_versions')
