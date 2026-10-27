"""kb categories

Giai đoạn 2 của ERD v5_1: sổ đăng ký danh mục ``kb_categories`` và khóa ngoại ``category_slug`` trỏ vào đó.

Phương án B cho 9 nhóm cũ của bộ luật bill: bốn nhóm không trùng slug backend (dining, groceries, utilities, healthcare) vào bảng như dòng bí danh
(is_active = false, replaced_by = slug backend), nên kb_brands và kb_keywords trỏ khóa ngoại được ngay mà không đổi từ vựng. CÁC CHECK liệt kê slug của các
bảng chỉ nhận slug backend được giữ nguyên (khóa ngoại một mình sẽ để dòng bí danh lọt vào). Dòng chính và bí danh là BẢN SAO CỐ ĐỊNH tại thời điểm
migration (không đọc code), để migration cũ không đổi nghĩa khi code đổi.

Khóa ngoại được thêm NOT VALID rồi VALIDATE: nếu dữ liệu cũ có slug lạ, migration dừng và nêu tên ràng buộc (DDL của Postgres nằm trong giao dịch nên không để lại dấu vết).

Revision ID: 0022
Revises: 0021
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '0022'
down_revision: Union[str, Sequence[str], None] = '0021'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None

# (slug, nhãn, is_active, replaced_by). Dòng chính trước: dòng bí danh trỏ tới chúng.
CATEGORIES = [
    ('food', 'Ăn uống', True, None),
    ('transport', 'Di chuyển', True, None),
    ('shopping', 'Mua sắm', True, None),
    ('education', 'Giáo dục', True, None),
    ('housing', 'Nhà ở', True, None),
    ('bills', 'Hóa đơn', True, None),
    ('entertainment', 'Giải trí', True, None),
    ('health', 'Sức khỏe', True, None),
    ('family', 'Gia đình', True, None),
    ('income', 'Thu nhập', True, None),
    ('other', 'Khác', True, None),
    ('dining', 'Ăn uống (nhóm cũ)', False, 'food'),
    ('groceries', 'Siêu thị (nhóm cũ)', False, 'food'),
    ('utilities', 'Tiện ích (nhóm cũ)', False, 'bills'),
    ('healthcare', 'Sức khỏe (nhóm cũ)', False, 'health'),
]

# (bảng, cột): ON UPDATE CASCADE ON DELETE RESTRICT.
FOREIGN_KEYS = [
    ('kb_brands', 'category_slug'),
    ('kb_keywords', 'category_slug'),
    ('kb_item_categories', 'category_slug'),
    ('user_merchant_memory', 'category_slug'),
    ('ml_training_samples', 'predicted_slug'),
    ('ml_training_samples', 'final_slug'),
    ('ops_analyze_log', 'category_slug'),
    ('ml_user_feedback', 'predicted_category'),
    ('ml_user_feedback', 'corrected_category'),
]


def upgrade() -> None:
    op.create_table('kb_categories',
    sa.Column('slug', sa.String(length=32), nullable=False),
    sa.Column('label', sa.Text(), nullable=False),
    sa.Column('taxonomy_version', sa.Integer(), server_default=sa.text('1'), nullable=False),
    sa.Column('is_active', sa.Boolean(), server_default=sa.text('true'), nullable=False),
    sa.Column('replaced_by', sa.String(length=32), nullable=True),
    sa.Column('synced_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint("slug ~ '^[a-z_]+$'", name='chk_kb_categories_slug'),
    sa.CheckConstraint('taxonomy_version >= 1', name='chk_kb_categories_taxonomy_version'),
    sa.CheckConstraint('replaced_by IS NULL OR replaced_by <> slug', name='chk_kb_categories_not_self'),
    sa.ForeignKeyConstraint(['replaced_by'], ['kb_categories.slug'], ondelete='SET NULL'),
    sa.PrimaryKeyConstraint('slug')
    )
    op.create_index('idx_kb_categories_replaced_by', 'kb_categories', ['replaced_by'], unique=False, postgresql_where=sa.text('replaced_by IS NOT NULL'))

    categories = sa.table('kb_categories', sa.column('slug', sa.String), sa.column('label', sa.Text), sa.column('is_active', sa.Boolean), sa.column('replaced_by', sa.String))
    op.bulk_insert(categories, [{'slug': s, 'label': label, 'is_active': active, 'replaced_by': target} for s, label, active, target in CATEGORIES])

    for table, column in FOREIGN_KEYS:
        name = f'{table}_{column}_fkey'
        op.execute(
            f"ALTER TABLE {table} ADD CONSTRAINT {name} FOREIGN KEY ({column}) "
            f"REFERENCES kb_categories (slug) ON UPDATE CASCADE ON DELETE RESTRICT NOT VALID"
        )
        op.execute(f"ALTER TABLE {table} VALIDATE CONSTRAINT {name}")


def downgrade() -> None:
    for table, column in reversed(FOREIGN_KEYS):
        op.drop_constraint(f'{table}_{column}_fkey', table, type_='foreignkey')
    op.drop_index('idx_kb_categories_replaced_by', table_name='kb_categories', postgresql_where=sa.text('replaced_by IS NOT NULL'))
    op.drop_table('kb_categories')
