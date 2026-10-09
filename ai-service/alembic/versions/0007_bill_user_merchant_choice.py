"""bill user merchant choice

Thêm ``bill_user_merchant_choice``: cửa hàng người dùng đã chốt danh mục KHI LƯU GIAO DỊCH, lưu đúng slug backend (11 slug) họ đã chọn.
CHỈ THÊM một bảng. Bảng cũ ``bill_user_merchant_category`` (9 nhóm của bộ luật bill) giữ nguyên và vẫn được tra sau bảng mới.

Vì sao bảng riêng: người dùng chỉ chọn được trong danh mục của backend, nên nhớ nguyên slug đó là không mất gì; đổi ngược về 9 nhóm thì mất
(``food`` là Ăn uống hay Siêu thị?). Chi tiết ở ``app/db/models/bill.py``.

Revision ID: 0007
Revises: 0006
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '0007'
down_revision: Union[str, Sequence[str], None] = '0006'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table('bill_user_merchant_choice',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('user_id_hash', sa.String(length=64), nullable=False),
    sa.Column('merchant_key', sa.Text(), nullable=False),
    sa.Column('display_name', sa.Text(), nullable=True),
    sa.Column('category_slug', sa.String(length=32), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint("category_slug IN ('food', 'transport', 'shopping', 'education', 'housing', 'bills', 'entertainment', 'health', 'family', 'other')", name='chk_bill_user_merchant_choice_slug'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('user_id_hash', 'merchant_key', name='uq_bill_user_merchant_choice')
    )


def downgrade() -> None:
    op.drop_table('bill_user_merchant_choice')
