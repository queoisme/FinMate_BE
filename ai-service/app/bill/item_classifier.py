"""Phân loại từng món trên hóa đơn (mục 5 của thiết kế tách bill theo món).

Thứ tự cho mỗi món: thừa kế món chính → luật/từ khóa (DB) → nhóm mặc định của cửa hàng → Gemini (chỉ tên, chỉ khi được phép) → unknown.
Chỉ điều phối, không chạm DB: route nạp sẵn ``instruction``. Không log tên món, cửa hàng hay kết quả của mô hình.
"""
from __future__ import annotations

import asyncio
from collections.abc import Sequence

from app.bill.classifier import _category_from_products, classify_category_detailed
from app.bill.gemini import MAX_NAMES, GeminiClient, GeminiFailed
from app.bill.knowledge import BillKnowledge
from app.core.category_map import SLUG_BY_LOCAL_CATEGORY
from app.schemas.bill import ClassifyItemIn, ClassifyItemOut

# Trần cứng cho cả lần gọi Gemini: timeout của httpx tính theo từng pha (kết nối, đọc...), không theo cả lần gọi.
GEMINI_HARD_LIMIT_SECONDS = 8.0


def _merchant_default(merchant: str | None) -> str | None:
    """Nhóm mặc định của cửa hàng (bảng thương hiệu, từ khóa trong tên cửa hàng), đã đổi sang slug BE."""
    if not merchant:
        return None
    category, source, _ = classify_category_detailed(merchant, [])
    return SLUG_BY_LOCAL_CATEGORY.get(category) if source in ("brand", "keyword") else None


async def classify_items(
    items: Sequence[ClassifyItemIn],
    merchant: str | None,
    knowledge: BillKnowledge,
    gemini: GeminiClient,
    instruction: str,
    allow_ai: bool,
) -> tuple[list[ClassifyItemOut], bool]:
    await knowledge.refresh_if_stale()  # nạp bảng thương hiệu/từ khóa từ DB vào classifier (TTL 60 giây)
    results: list[tuple[str | None, str] | None] = [None] * len(items)  # (slug, nguồn); None = chưa quyết

    # Bước 2: luật/từ khóa theo đúng tên món. Món thuốc dừng ở đây nên không bao giờ gửi ra ngoài.
    for i, item in enumerate(items):
        if item.parent is None:
            hit = _category_from_products([item.name], None)
            slug = SLUG_BY_LOCAL_CATEGORY.get(hit[0]) if hit else None
            if slug:
                results[i] = (slug, "rule")

    # Bước 3: nhóm mặc định của cửa hàng, chỉ cho món chưa khớp luật.
    default = _merchant_default(merchant)
    if default:
        for i, item in enumerate(items):
            if item.parent is None and results[i] is None:
                results[i] = (default, "merchant")

    # Bước 4: Gemini cho tối đa 20 món còn lại đầu tiên, chỉ tên.
    ai_used = False
    pending = [i for i, item in enumerate(items) if item.parent is None and results[i] is None][:MAX_NAMES]
    if pending and allow_ai and instruction.strip() and gemini.available:
        try:
            async with asyncio.timeout(GEMINI_HARD_LIMIT_SECONDS):
                answers = await gemini.classify([items[i].name for i in pending], instruction)
        except (GeminiFailed, TimeoutError):
            answers = None  # món lạ thành unknown; không bao giờ ném lỗi ra cho client
        if answers is not None:
            ai_used = True
            for i, category in zip(pending, answers):
                if category != "unknown":
                    results[i] = (category, "ai")

    # Bước 1 (làm sau cùng vì cần kết quả CUỐI của món chính) và bước 5.
    out: list[ClassifyItemOut] = []
    for i, item in enumerate(items):
        if item.parent is not None:
            parent = results[item.parent]  # parent < i (schema đã kiểm) nên đã quyết xong
            results[i] = (parent[0], "inherit") if parent else None
        slug, source = results[i] or (None, "unknown")
        out.append(ClassifyItemOut(index=i, category_slug=slug, source=source))
    return out, ai_used
