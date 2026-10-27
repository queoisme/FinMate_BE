"""category kind

Thêm danh mục backend mới không còn phải sửa cả chục CHECK: ``kb_categories`` có cột ``kind`` (expense | income | legacy), bảy CHECK liệt kê slug được thay bằng MỘT hàm trigger
kiểm LOẠI danh mục (``app/db/category_guard.py``; khóa ngoại vẫn lo "slug có tồn tại"). Dòng bí danh của 9 nhóm cũ có ``kind = 'legacy'`` nên vẫn không lọt vào các bảng chỉ nhận
slug của backend, y như CHECK cũ. CHECK ``kb_audit_log.resource`` nhận thêm ``category`` (nhật ký của việc đồng bộ danh mục từ backend).

Dữ liệu cũ không bị kiểm lại (đã hợp lệ theo CHECK cũ). Bước lùi dựng lại bảy CHECK với danh sách slug lúc này và kiểm ngay: nếu đã có danh mục mới ngoài danh sách thì bước lùi dừng.

Revision ID: 0026
Revises: 0025
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa

from app.db import category_guard


# revision identifiers, used by Alembic.
revision: str = '0026'
down_revision: Union[str, Sequence[str], None] = '0025'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None

ALL = "'food', 'transport', 'shopping', 'education', 'housing', 'bills', 'entertainment', 'health', 'family', 'income', 'other'"
SPEND = "'food', 'transport', 'shopping', 'education', 'housing', 'bills', 'entertainment', 'health', 'family', 'other'"
# (bảng, tên CHECK, điều kiện lúc trước): dùng cho bước lùi.
OLD_CHECKS = [
    ('kb_item_categories', 'chk_bill_item_cache_slug', f'category_slug IN ({SPEND})'),
    ('user_merchant_memory', 'chk_bill_user_merchant_choice_slug', f'category_slug IN ({SPEND})'),
    ('ml_training_samples', 'chk_bill_training_samples_final_slug', f'final_slug IS NULL OR final_slug IN ({SPEND})'),
    ('ops_analyze_log', 'chk_pipeline_requests_category_slug', f'category_slug IS NULL OR category_slug IN ({ALL})'),
    ('ml_user_feedback', 'chk_user_feedback_predicted_category', f'predicted_category IS NULL OR predicted_category IN ({ALL})'),
    ('ml_user_feedback', 'chk_user_feedback_corrected_category', f'corrected_category IS NULL OR corrected_category IN ({ALL})'),
    ('ml_item_feedback', 'chk_ml_item_feedback_slug', f'category_slug IN ({SPEND})'),
]
AUDIT_OLD = "resource IN ('brand', 'keyword', 'item_cache', 'instruction', 'provider_format')"
AUDIT_NEW = "resource IN ('brand', 'keyword', 'item_cache', 'instruction', 'provider_format', 'category')"


def _recheck(table: str, name: str, expr: str) -> None:
    op.execute(f"ALTER TABLE {table} ADD CONSTRAINT {name} CHECK ({expr}) NOT VALID")
    op.execute(f"ALTER TABLE {table} VALIDATE CONSTRAINT {name}")


def upgrade() -> None:
    op.add_column('kb_categories', sa.Column('kind', sa.String(length=8), server_default=sa.text("'expense'"), nullable=False))
    op.execute("UPDATE kb_categories SET kind = 'income' WHERE slug = 'income'")
    op.execute("UPDATE kb_categories SET kind = 'legacy' WHERE NOT is_active AND replaced_by IS NOT NULL")
    _recheck('kb_categories', 'chk_kb_categories_kind', "kind IN ('expense', 'income', 'legacy')")

    for table, name, _ in OLD_CHECKS:
        op.execute(f"ALTER TABLE {table} DROP CONSTRAINT {name}")
    for statement in category_guard.OBJECTS:
        op.execute(statement)

    op.execute("ALTER TABLE kb_audit_log DROP CONSTRAINT chk_kb_audit_log_resource")
    _recheck('kb_audit_log', 'chk_kb_audit_log_resource', AUDIT_NEW)


def downgrade() -> None:
    op.execute("ALTER TABLE kb_audit_log DROP CONSTRAINT chk_kb_audit_log_resource")
    _recheck('kb_audit_log', 'chk_kb_audit_log_resource', AUDIT_OLD)

    for statement in category_guard.DROP_OBJECTS:
        op.execute(statement)
    for table, name, expr in OLD_CHECKS:
        _recheck(table, name, expr)

    op.execute("ALTER TABLE kb_categories DROP CONSTRAINT chk_kb_categories_kind")
    op.drop_column('kb_categories', 'kind')
