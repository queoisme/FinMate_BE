"""bill ai instructions v2: định nghĩa nhóm rõ hơn (khăn ướt, đồ dùng học tập, thẻ nạp điện thoại)

Hóa đơn thử ngày 2026-10-15 cho thấy các định nghĩa chồng lấn: khăn ướt ra Mua sắm trong khi tã và sữa bột ra Gia đình; vở, bút, balo học sinh ra Mua sắm
trong khi sách ra Giáo dục. ``ITEM_RULES_TEXT`` đã sửa (bản seed cho cài mới). Migration này đổi dòng đang dùng trong DB sang bản mới CHỈ KHI nó còn đúng là
bản seed cũ (md5 bên dưới): nội dung sửa tay trong DB là của người vận hành, không bị ghi đè.

Khi đã đổi định nghĩa thì ``bill_item_cache`` (câu trả lời của Gemini theo định nghĩa cũ, "câu trả lời đầu tiên thắng") sai thời, nên xóa để hỏi lại.
Cài mới: 0008 đã seed thẳng bản mới nên md5 không khớp, migration này không làm gì. Hạ cấp không khôi phục chữ cũ (nội dung chỉ dẫn là dữ liệu).

Revision ID: 0011
Revises: 0010
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa

from app.bill.item_rules_seed import ITEM_RULES_TEXT


# revision identifiers, used by Alembic.
revision: str = '0011'
down_revision: Union[str, Sequence[str], None] = '0010'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None

OLD_SEED_MD5 = 'fda00d3b686430fb640246ca0ccfe5d4'  # md5 của ITEM_RULES_TEXT lúc migration 0008 seed


def upgrade() -> None:
    bind = op.get_bind()
    replaced = bind.execute(
        sa.text("UPDATE bill_ai_instructions SET instruction_text = :new, updated_at = now() WHERE is_active AND md5(instruction_text) = :old")
        .bindparams(new=ITEM_RULES_TEXT, old=OLD_SEED_MD5)
    ).rowcount
    if replaced:
        bind.execute(sa.text("DELETE FROM bill_item_cache"))


def downgrade() -> None:
    pass
