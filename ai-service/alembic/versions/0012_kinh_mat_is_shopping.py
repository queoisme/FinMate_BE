"""kính mát là đồ thời trang (Mua sắm), không phải Sức khỏe

Luật 3 của chỉ dẫn đã duyệt: kính mắt đo độ (gọng, tròng) là health, kính mát thời trang là shopping. Bảng từ khóa sản phẩm trong DB còn "kinh mat"
ở healthcare nên luật xếp kính mát vào Sức khỏe, lệch với Gemini và với định nghĩa. Chuyển sang shopping theo kiểu an toàn xung đột (như 0008):
chèn dòng đích nếu chưa có rồi xóa dòng nguồn. Chạy lại không lỗi.

Revision ID: 0012
Revises: 0011
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '0012'
down_revision: Union[str, Sequence[str], None] = '0011'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def _move(source: str, target: str) -> None:
    bind = op.get_bind()
    for kind in ('product', 'chat'):  # 'chat' là bản sao của từ điển chat; bộ phân loại không đọc nhưng để lại thì hai bảng mâu thuẫn nhau
        bind.execute(sa.text(
            "INSERT INTO bill_keywords (kind, category_slug, keyword, category_priority, is_active, source) "
            "SELECT s.kind, :target, s.keyword, "
            "COALESCE((SELECT MIN(t.category_priority) FROM bill_keywords t WHERE t.kind = s.kind AND t.category_slug = :target), 100), "
            "s.is_active, s.source "
            "FROM bill_keywords s WHERE s.kind = :kind AND s.category_slug = :source AND s.keyword = 'kinh mat' "
            "ON CONFLICT (kind, category_slug, keyword) DO NOTHING"
        ).bindparams(target=target, source=source, kind=kind))
        bind.execute(sa.text(
            "DELETE FROM bill_keywords WHERE kind = :kind AND category_slug = :source AND keyword = 'kinh mat'"
        ).bindparams(source=source, kind=kind))


def upgrade() -> None:
    _move('healthcare', 'shopping')


def downgrade() -> None:
    _move('shopping', 'healthcare')
