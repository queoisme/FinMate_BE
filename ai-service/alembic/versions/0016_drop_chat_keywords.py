"""drop chat keywords

Gỡ loại ``chat`` khỏi ``bill_keywords``: đó là bản chép của từ điển chat (seed từ ``ocr-service/data/chat_keywords.json``) mà không dịch vụ nào đọc lúc chạy và đã lệch
khỏi ``ai-service/dictionaries/*.json``. Từ điển chat giờ có đúng một nguồn: các tệp đó (bản mặc định) và tài liệu ``chat_dictionary`` trong ``config_documents`` (bản admin sửa).
XÓA các dòng ``kind = 'chat'`` rồi siết ràng buộc CHECK còn ``merchant``/``product``. Hạ cấp chỉ mở lại ràng buộc, KHÔNG khôi phục các dòng đã xóa
(nội dung gốc luôn dựng lại được từ ``dictionaries/``).

Revision ID: 0016
Revises: 0015
"""
from typing import Sequence, Union

from alembic import op


# revision identifiers, used by Alembic.
revision: str = '0016'
down_revision: Union[str, Sequence[str], None] = '0015'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.execute("DELETE FROM bill_keywords WHERE kind = 'chat'")
    op.drop_constraint('chk_bill_keywords_kind', 'bill_keywords', type_='check')
    op.create_check_constraint('chk_bill_keywords_kind', 'bill_keywords', "kind IN ('merchant', 'product')")


def downgrade() -> None:
    op.drop_constraint('chk_bill_keywords_kind', 'bill_keywords', type_='check')
    op.create_check_constraint('chk_bill_keywords_kind', 'bill_keywords', "kind IN ('merchant', 'product', 'chat')")
