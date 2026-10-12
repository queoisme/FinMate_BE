"""bill item cache

Thêm ``bill_item_cache``: tên món (đã chuẩn hóa) -> slug mà Gemini đã trả lời, để lần sau không phải hỏi lại. Dùng chung, KHÔNG có user_id_hash.
CHỈ THÊM một bảng.

Revision ID: 0009
Revises: 0008
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '0009'
down_revision: Union[str, Sequence[str], None] = '0008'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table('bill_item_cache',
    sa.Column('item_key', sa.Text(), nullable=False),
    sa.Column('category_slug', sa.String(length=32), nullable=False),
    sa.Column('hits', sa.Integer(), server_default=sa.text('1'), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint("category_slug IN ('food', 'transport', 'shopping', 'education', 'housing', 'bills', 'entertainment', 'health', 'family', 'other')", name='chk_bill_item_cache_slug'),
    sa.PrimaryKeyConstraint('item_key')
    )


def downgrade() -> None:
    op.drop_table('bill_item_cache')
