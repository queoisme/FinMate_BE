"""Thêm thương hiệu và từ khóa mặc định (các bảng trong code) vào ``kb_brands`` / ``kb_keywords`` lúc khởi động.

Chỉ THÊM, DB luôn thắng: dòng đã có (đã sửa, đã tắt ``is_active = false``) không bị ghi đè hay bật lại, và một từ khóa đã có dòng ở
BẤT KỲ nhóm nào của cùng loại (migration 0008/0012 hay người duyệt chuyển nhóm) thì không được thêm lại ở nhóm cũ. Nhờ vậy chuỗi mới
thêm vào code đến được mọi môi trường sau lần deploy kế tiếp, mà không phải chạy script tay. Muốn bỏ một mục thì TẮT nó trong DB,
đừng xóa dòng (xóa rồi lần khởi động sau sẽ thêm lại).

ai-service sở hữu hai loại ``merchant`` và ``product``. Loại ``chat`` chỉ ocr-service dùng nên do ocr-service/scripts/seed_knowledge_to_pg.py nạp.
"""

from __future__ import annotations

import structlog
from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

from app.bill import brands, classifier

logger = structlog.get_logger(__name__)

# Chụp ngay khi nạp module. ``apply_rows`` (nạp từ DB) sửa các bảng trong code TẠI CHỖ, nên đọc lúc seed chạy là đọc lại chính DB
# và "mặc định" trôi theo DB. main.py import module này trước khi nhận request đầu tiên.
_BRANDS = tuple(
    (key, category, brands.BRAND_DISPLAY.get(key, key.title()), key in brands.WHOLE_WORD_BRANDS)
    for key, category in brands.BRAND_CATEGORIES.items()
)
# category_priority theo thứ tự nhóm trong bảng (nhóm đứng trước được xét trước), cùng cách ocr-service seed.
_KEYWORDS = tuple(
    (kind, category, word, (rank + 1) * 10)
    for kind, table in (("merchant", classifier._CATEGORY_KEYWORDS), ("product", classifier._PRODUCT_KEYWORDS))
    for rank, (category, words) in enumerate(table.items())
    for word in words
)

_INSERT_BRAND = text(
    "INSERT INTO kb_brands (brand_key, category_slug, display_name, whole_word, is_active, source) "
    "VALUES (:key, :category, :display, :whole, TRUE, 'seed') ON CONFLICT (brand_key) DO NOTHING"
)
_INSERT_KEYWORD = text(
    "INSERT INTO kb_keywords (kind, category_slug, keyword, category_priority, is_active, source) "
    "VALUES (:kind, :category, :word, :priority, TRUE, 'seed') ON CONFLICT (kind, category_slug, keyword) DO NOTHING"
)


def default_brands() -> tuple[tuple[str, str, str, bool], ...]:
    """(khóa, nhóm, tên hiển thị, chỉ khớp nguyên từ)"""
    return _BRANDS


def default_keywords() -> tuple[tuple[str, str, str, int], ...]:
    """(loại, nhóm, từ khóa, độ ưu tiên nhóm)"""
    return _KEYWORDS


async def seed_bill_knowledge(session: AsyncSession) -> dict[str, int]:
    """Thêm mục còn thiếu, trả về số dòng mới. Chạy lại bao nhiêu lần cũng không đổi gì."""
    known_brands = {row[0] for row in await session.execute(text("SELECT brand_key FROM kb_brands"))}
    # Không xét nhóm: từ đã được chuyển sang nhóm khác thì không thêm lại ở nhóm cũ.
    known_keywords = {(row[0], row[1]) for row in await session.execute(text("SELECT kind, keyword FROM kb_keywords"))}

    new_brands = [
        {"key": key, "category": category, "display": display, "whole": whole}
        for key, category, display, whole in _BRANDS
        if key not in known_brands
    ]
    new_keywords = [
        {"kind": kind, "category": category, "word": word, "priority": priority}
        for kind, category, word, priority in _KEYWORDS
        if (kind, word) not in known_keywords
    ]
    if new_brands:
        await session.execute(_INSERT_BRAND, new_brands)
    if new_keywords:
        await session.execute(_INSERT_KEYWORD, new_keywords)
    await session.commit()
    return {"brands": len(new_brands), "keywords": len(new_keywords)}
