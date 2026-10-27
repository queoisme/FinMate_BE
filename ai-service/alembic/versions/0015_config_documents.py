"""config documents

Thêm ``config_documents``: tài liệu cấu hình JSON có phiên bản (từ vựng hiểu câu chat, từ vựng và ngưỡng đọc hóa đơn) sửa được từ trang quản trị mà không phải
build lại. Mỗi lần lưu là một dòng mới; partial unique index ``uq_config_documents_active`` bảo đảm mỗi tài liệu có đúng một bản đang chạy.
CHỈ THÊM một bảng.

Revision ID: 0015
Revises: 0014
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql


# revision identifiers, used by Alembic.
revision: str = '0015'
down_revision: Union[str, Sequence[str], None] = '0014'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table('config_documents',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('name', sa.String(length=48), nullable=False),
    sa.Column('version', sa.Integer(), nullable=False),
    sa.Column('status', sa.String(length=10), nullable=False),
    sa.Column('content', postgresql.JSONB(astext_type=sa.Text()), nullable=False),
    sa.Column('note', sa.Text(), nullable=True),
    sa.Column('created_by', sa.String(length=64), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('activated_at', sa.DateTime(timezone=True), nullable=True),
    sa.CheckConstraint("status IN ('draft', 'active', 'archived')", name='chk_config_documents_status'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('name', 'version', name='uq_config_documents_name_version')
    )
    op.create_index('uq_config_documents_active', 'config_documents', ['name'], unique=True, postgresql_where=sa.text("status = 'active'"))


def downgrade() -> None:
    op.drop_index('uq_config_documents_active', table_name='config_documents', postgresql_where=sa.text("status = 'active'"))
    op.drop_table('config_documents')
