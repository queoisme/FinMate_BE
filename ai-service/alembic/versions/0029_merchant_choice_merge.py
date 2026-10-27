"""merchant choice merge

Bỏ ``bill_user_merchant_category`` (cửa hàng người dùng đã chốt theo 9 nhóm cũ của bộ luật bill, chỉ ocr-service ghi) và view ``bill_merchant_votes`` đọc từ nó. Nội dung chuyển sang
``user_merchant_memory`` (slug backend, một bảng duy nhất cho "cửa hàng người dùng đã chốt"): nhóm cũ đổi qua ``kb_categories.replaced_by`` (dining, groceries -> food; utilities -> bills;
healthcare -> health; các nhóm trùng slug backend giữ nguyên). Cùng (người dùng, cửa hàng) đã có ở ``user_merchant_memory`` (người dùng chọn lúc lưu giao dịch) thì bản đó thắng.
Mã người dùng không phải SHA-256 hex (ocr-service từng mặc định ``local``) được băm lại.

``bill_unknown_scans`` (hàng đợi duyệt cửa hàng lạ) được GIỮ, là bảng ngoài ERD có chủ đích: thêm CHECK mã băm hex 64 cho ``user_id_hash`` và ``content_hash`` (băm lại mã người dùng không đúng dạng).

Bước lùi dựng lại ``bill_user_merchant_category`` và view RỖNG: không đổi ngược được slug backend về 9 nhóm (food có thể là dining hay groceries), nên dữ liệu đã gộp ở lại ``user_merchant_memory``.

Revision ID: 0029
Revises: 0028
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa

from app.db.bill_views import MERCHANT_VOTES_VIEW


# revision identifiers, used by Alembic.
revision: str = '0029'
down_revision: Union[str, Sequence[str], None] = '0028'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None

HEX = "~ '^[0-9a-f]{64}$'"
REHASH = "encode(sha256(convert_to({col}, 'UTF8')), 'hex')"
UNKNOWN_CHECKS = [
    ('chk_bill_unknown_scans_user_hash', f"user_id_hash {HEX}"),
    ('chk_bill_unknown_scans_content_hash', f"content_hash {HEX}"),
]


def upgrade() -> None:
    op.execute(
        "INSERT INTO user_merchant_memory (user_id_hash, merchant_key, display_name, category_slug, created_at, updated_at) "
        f"SELECT CASE WHEN b.user_id_hash {HEX} THEN b.user_id_hash ELSE {REHASH.format(col='b.user_id_hash')} END, "
        "lower(b.merchant_key), b.display_name, COALESCE(c.replaced_by, c.slug), b.created_at, b.updated_at "
        "FROM bill_user_merchant_category b JOIN kb_categories c ON c.slug = b.category_slug "
        "ORDER BY b.updated_at DESC "  # nhiều dòng cũ cùng thành một khóa sau lower(): dòng mới nhất được chèn trước nên thắng
        "ON CONFLICT (user_id_hash, merchant_key) DO NOTHING"
    )
    op.execute("DROP VIEW IF EXISTS bill_merchant_votes")
    op.drop_index('idx_bill_user_merchant_vote', table_name='bill_user_merchant_category')
    op.drop_table('bill_user_merchant_category')

    op.execute(f"UPDATE bill_unknown_scans SET user_id_hash = {REHASH.format(col='user_id_hash')} WHERE user_id_hash !{HEX}")
    for name, expr in UNKNOWN_CHECKS:
        op.execute(f"ALTER TABLE bill_unknown_scans ADD CONSTRAINT {name} CHECK ({expr}) NOT VALID")
        op.execute(f"ALTER TABLE bill_unknown_scans VALIDATE CONSTRAINT {name}")


def downgrade() -> None:
    for name, _ in reversed(UNKNOWN_CHECKS):
        op.drop_constraint(name, 'bill_unknown_scans', type_='check')

    op.create_table('bill_user_merchant_category',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('user_id_hash', sa.String(length=64), nullable=False),
    sa.Column('merchant_key', sa.Text(), nullable=False),
    sa.Column('display_name', sa.Text(), nullable=True),
    sa.Column('category_slug', sa.String(length=32), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('user_id_hash', 'merchant_key', name='uq_bill_user_merchant')
    )
    op.create_index('idx_bill_user_merchant_vote', 'bill_user_merchant_category', ['merchant_key', 'category_slug'], unique=False)
    op.execute(MERCHANT_VOTES_VIEW)
