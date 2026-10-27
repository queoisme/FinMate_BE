"""Dữ liệu ban đầu của ``kb_categories``: 11 slug backend (``CATEGORY_SLUGS``) + dòng bí danh cho nhóm cũ của bộ luật bill (phương án B).

Migration 0022 giữ BẢN SAO CỐ ĐỊNH của danh sách này (migration không đổi theo code); module này dành cho test và công cụ cần dựng lại bảng.
Thêm danh mục backend mới: backend gọi ``PUT /api/v1/admin/categories/sync`` (không cần migration, không cần deploy). Muốn mô hình/Gemini cũng đoán được nó thì mới cần thêm vào ``CATEGORY_SLUGS`` + ``LABELS`` ở đây (test ``test_category_seed`` nhắc nếu quên).
"""

from sqlalchemy import text

from app.core.category_map import SLUG_BY_LOCAL_CATEGORY
from app.core.enums import CATEGORY_SLUGS

# Khớp ``CategorySeeder.cs`` ở backend.
LABELS: dict[str, str] = {
    "food": "Ăn uống",
    "transport": "Di chuyển",
    "shopping": "Mua sắm",
    "education": "Giáo dục",
    "housing": "Nhà ở",
    "bills": "Hóa đơn",
    "entertainment": "Giải trí",
    "health": "Sức khỏe",
    "family": "Gia đình",
    "income": "Thu nhập",
    "other": "Khác",
}
LEGACY_LABELS: dict[str, str] = {
    "dining": "Ăn uống (nhóm cũ)",
    "groceries": "Siêu thị (nhóm cũ)",
    "utilities": "Tiện ích (nhóm cũ)",
    "healthcare": "Sức khỏe (nhóm cũ)",
}


def category_rows() -> list[tuple[str, str, bool, str | None, str]]:
    """``(slug, label, is_active, replaced_by, kind)``, dòng chính trước để khóa ngoại tự tham chiếu của dòng bí danh thỏa."""
    rows = [(slug, LABELS[slug], True, None, "income" if slug == "income" else "expense") for slug in CATEGORY_SLUGS]
    aliases = [
        (group, LEGACY_LABELS.get(group, f"{group} (nhóm cũ)"), False, backend, "legacy")
        for group, backend in SLUG_BY_LOCAL_CATEGORY.items()
        if group not in CATEGORY_SLUGS
    ]
    return rows + aliases


INSERT_CATEGORY = text(
    "INSERT INTO kb_categories (slug, label, is_active, replaced_by, kind) VALUES (:slug, :label, :is_active, :replaced_by, :kind) ON CONFLICT (slug) DO NOTHING"
)


async def seed_categories(connection) -> None:
    for slug, label, is_active, replaced_by, kind in category_rows():
        await connection.execute(INSERT_CATEGORY, {"slug": slug, "label": label, "is_active": is_active, "replaced_by": replaced_by, "kind": kind})
