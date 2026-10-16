"""bill rule audit

Thêm ``bill_rule_audit``: nhật ký thay đổi luật phân loại qua API quản trị (trước/sau, người đổi dạng băm, lý do), dùng để xem lịch sử và hoàn tác.
CHỈ THÊM một bảng.

Revision ID: 0014
Revises: 0013
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql


# revision identifiers, used by Alembic.
revision: str = '0014'
down_revision: Union[str, Sequence[str], None] = '0013'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table('bill_rule_audit',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('resource', sa.String(length=16), nullable=False),
    sa.Column('resource_key', sa.Text(), nullable=False),
    sa.Column('action', sa.String(length=12), nullable=False),
    sa.Column('before', postgresql.JSONB(astext_type=sa.Text()), nullable=True),
    sa.Column('after', postgresql.JSONB(astext_type=sa.Text()), nullable=True),
    sa.Column('actor_hash', sa.String(length=64), nullable=False),
    sa.Column('reason', sa.Text(), nullable=True),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.PrimaryKeyConstraint('id')
    )
    op.create_index('idx_bill_rule_audit_created', 'bill_rule_audit', ['created_at'], unique=False)
    op.create_index('idx_bill_rule_audit_resource', 'bill_rule_audit', ['resource', 'resource_key'], unique=False)


def downgrade() -> None:
    op.drop_index('idx_bill_rule_audit_resource', table_name='bill_rule_audit')
    op.drop_index('idx_bill_rule_audit_created', table_name='bill_rule_audit')
    op.drop_table('bill_rule_audit')
