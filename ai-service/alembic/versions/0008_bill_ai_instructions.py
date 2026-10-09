"""bill ai instructions

Thêm ``bill_ai_instructions``: luật phân loại từng món cho Gemini, sửa ở DB không cần build lại. Đúng một dòng ``is_active``.
Seed một dòng active từ ``ITEM_RULES_TEXT``.

Bước dữ liệu: bột giặt, nước rửa chén, giấy vệ sinh, nước xả vải là ``shopping`` (khớp luật mới của từng món), nên chuyển bốn từ khóa
đó của ``bill_keywords`` từ groceries sang shopping. ``bill_keywords`` có UNIQUE (kind, category_slug, keyword) nên làm kiểu an toàn xung đột:
chèn dòng đích nếu chưa có (priority lấy từ chính nhóm đích) rồi xóa dòng nguồn. Chạy lại không lỗi.

Revision ID: 0008
Revises: 0007
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa

from app.bill.item_rules_seed import ITEM_RULES_TEXT


# revision identifiers, used by Alembic.
revision: str = '0008'
down_revision: Union[str, Sequence[str], None] = '0007'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None

HOUSEHOLD_KEYWORDS = ('bot giat', 'nuoc rua chen', 'giay ve sinh', 'nuoc xa vai')


def _move_keywords(source: str, target: str) -> None:
    keywords = sa.bindparam('keywords', value=list(HOUSEHOLD_KEYWORDS), type_=sa.ARRAY(sa.Text()))
    op.get_bind().execute(sa.text(
        "INSERT INTO bill_keywords (kind, category_slug, keyword, category_priority, is_active, source) "
        "SELECT 'product', :target, s.keyword, "
        # priority của nhóm đích (mọi dòng product của nhóm có cùng số); nhóm đích chưa có dòng nào thì dùng mặc định 100
        "COALESCE((SELECT MIN(t.category_priority) FROM bill_keywords t WHERE t.kind = 'product' AND t.category_slug = :target), 100), "
        "s.is_active, s.source "
        "FROM bill_keywords s WHERE s.kind = 'product' AND s.category_slug = :source AND s.keyword = ANY(:keywords) "
        "ON CONFLICT (kind, category_slug, keyword) DO NOTHING"
    ).bindparams(keywords, target=target, source=source))
    op.get_bind().execute(sa.text(
        "DELETE FROM bill_keywords WHERE kind = 'product' AND category_slug = :source AND keyword = ANY(:keywords)"
    ).bindparams(keywords, source=source))


def upgrade() -> None:
    table = op.create_table('bill_ai_instructions',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('instruction_text', sa.Text(), nullable=False),
    sa.Column('is_active', sa.Boolean(), server_default=sa.text('true'), nullable=False),
    sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.PrimaryKeyConstraint('id')
    )
    op.create_index('uq_bill_ai_instructions_one_active', 'bill_ai_instructions', ['is_active'], unique=True, postgresql_where=sa.text('is_active'))
    op.bulk_insert(table, [{'instruction_text': ITEM_RULES_TEXT, 'is_active': True}])
    _move_keywords('groceries', 'shopping')


def downgrade() -> None:
    _move_keywords('shopping', 'groceries')
    op.drop_index('uq_bill_ai_instructions_one_active', table_name='bill_ai_instructions', postgresql_where=sa.text('is_active'))
    op.drop_table('bill_ai_instructions')
