"""``kb_categories`` — sổ đăng ký danh mục. Nguồn sự thật là danh mục của backend (``CategorySeeder``); AI giữ bản sao để các bảng khác trỏ khóa ngoại vào.

Phương án B cho 9 nhóm cũ của bộ luật bill: bốn nhóm không trùng slug backend (``dining``, ``groceries``, ``utilities``, ``healthcare``) nằm ở đây
như DÒNG BÍ DANH (``is_active = false``, ``replaced_by`` trỏ tới slug backend), nên ``kb_brands`` và ``kb_keywords`` trỏ khóa ngoại được ngay mà không phải
đổi từ vựng. Năm nhóm còn lại trùng slug backend nên là dòng chính. Khi từ vựng cũ bị gỡ hết, chỉ cần xóa các dòng bí danh.

Các bảng chỉ nhận slug backend không còn CHECK liệt kê slug: khóa ngoại lo "slug có tồn tại", trigger ``app.db.category_guard`` lo "đúng LOẠI" (``kind``), nên dòng bí danh (``legacy``) không lọt vào
và thêm danh mục backend mới chỉ là INSERT một dòng ở đây (xem ``app.services.category_sync``).
"""

from sqlalchemy import CheckConstraint, ForeignKey, Index, Integer, String, Text, text
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, created_at, timestamptz


def category_fk() -> ForeignKey:
    """Khóa ngoại tới ``kb_categories.slug``: đổi slug thì cuốn theo, xóa danh mục đang được dùng thì bị chặn."""
    return ForeignKey("kb_categories.slug", onupdate="CASCADE", ondelete="RESTRICT")


class KbCategory(Base):
    __tablename__ = "kb_categories"

    slug: Mapped[str] = mapped_column(String(32), primary_key=True)
    label: Mapped[str] = mapped_column(Text, nullable=False)
    taxonomy_version: Mapped[int] = mapped_column(Integer, nullable=False, server_default=text("1"))
    # expense = danh mục chi; income = thu nhập; legacy = dòng bí danh của 9 nhóm cũ (không bao giờ được chọn mới). Bảng khác khai báo LOẠI nào nó nhận ở ``app.db.category_guard``.
    kind: Mapped[str] = mapped_column(String(8), nullable=False, server_default=text("'expense'"))
    is_active: Mapped[bool] = mapped_column(nullable=False, server_default=text("true"))
    # Dòng bí danh / danh mục đã bị thay: trỏ tới slug đang dùng. SET NULL để xóa dòng đích không kéo theo lỗi, nhưng nên xóa bí danh trước.
    replaced_by: Mapped[str | None] = mapped_column(String(32), ForeignKey("kb_categories.slug", ondelete="SET NULL"))
    synced_at: Mapped[timestamptz] = mapped_column(nullable=False, server_default=text("now()"))
    created_at: Mapped[created_at]

    __table_args__ = (
        CheckConstraint("slug ~ '^[a-z_]+$'", name="chk_kb_categories_slug"),
        CheckConstraint("taxonomy_version >= 1", name="chk_kb_categories_taxonomy_version"),
        CheckConstraint("kind IN ('expense', 'income', 'legacy')", name="chk_kb_categories_kind"),
        CheckConstraint("replaced_by IS NULL OR replaced_by <> slug", name="chk_kb_categories_not_self"),
        Index("idx_kb_categories_replaced_by", "replaced_by", postgresql_where=text("replaced_by IS NOT NULL")),
    )


__all__ = ["KbCategory", "category_fk"]
