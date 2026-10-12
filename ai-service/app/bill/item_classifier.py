"""Phân loại từng món trên hóa đơn (mục 5 của thiết kế tách bill theo món).

Thứ tự cho mỗi món: thừa kế món chính → luật/từ khóa (DB) → Gemini (chỉ tên, chỉ khi được phép) → nhóm mặc định của cửa hàng → unknown.
Gemini đứng TRƯỚC nhóm mặc định: cửa hàng tiện lợi bán cả đồ ăn lẫn khăn ướt, nên "mặc định là ăn uống" đoán sai một cách tự tin.
Chỉ điều phối, không chạm DB: route nạp sẵn ``instruction``. Không log tên món, cửa hàng hay kết quả của mô hình.
"""
from __future__ import annotations

import re
from collections.abc import Sequence

from app.bill.classifier import _category_from_products, classify_category_detailed
from app.bill.gemini import MAX_NAMES, GeminiClient, GeminiFailed
from app.bill.knowledge import BillKnowledge
from app.core.category_map import SLUG_BY_LOCAL_CATEGORY
from app.schemas.bill import ClassifyItemIn, ClassifyItemOut

# Số tiền kiểu 45,000 / 1.250.000 (kèm đ/d/vnd dính trước hoặc sau): ô tên đôi khi dính giá, không được gửi giá ra ngoài.
_MONEY_RE = re.compile(r"(?<!\w)(?:(?:vnd|đ|d)\s?)?\d{1,3}(?:[.,]\d{3})+(?:\s?(?:vnd|đ|d))?(?!\w)", re.IGNORECASE)


def _strip_money(name: str) -> str:
    return " ".join(_MONEY_RE.sub(" ", name).split())


def _merchant_default(merchant: str | None) -> str | None:
    """Nhóm mặc định của THƯƠNG HIỆU (bảng bill_brands), đã đổi sang slug BE.

    Không dùng từ khóa chung trong tên cửa hàng ("mart", "nhà thuốc", "shop"): khớp nhầm ("Smartphone Center" → food) sẽ gắn nhóm
    nguồn ``merchant`` cho mọi món lạ và chặn luôn Gemini. Với lines=[] nguồn "brand" chỉ có thể đến từ tên cửa hàng khớp bảng thương hiệu.
    """
    if not merchant:
        return None
    category, source, _ = classify_category_detailed(merchant, [])
    return SLUG_BY_LOCAL_CATEGORY.get(category) if source == "brand" else None


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

    # Bước 2: luật/từ khóa theo đúng tên món.
    for i, item in enumerate(items):
        if item.parent is None:
            hit = _category_from_products([item.name], None)
            slug = SLUG_BY_LOCAL_CATEGORY.get(hit[0]) if hit else None
            if slug:
                results[i] = (slug, "rule")

    default = _merchant_default(merchant)

    # Bước 3: Gemini cho tối đa 20 món còn lại đầu tiên, chỉ tên (đã bỏ số tiền; tên rỗng sau khi bỏ thì không gửi).
    # Hóa đơn thuốc không bao giờ gửi: bỏ qua Gemini nếu cả hóa đơn (cửa hàng + mọi tên món) xếp vào healthcare,
    # nếu nhóm mặc định của thương hiệu là health, hoặc có bất kỳ món nào đã ra health bằng luật.
    ai_used = False
    bill_category, bill_source, _ = classify_category_detailed(merchant, [item.name for item in items])
    health_bill = (
        (SLUG_BY_LOCAL_CATEGORY.get(bill_category) == "health" and bill_source != "none")
        or default == "health"
        or any(r is not None and r[0] == "health" for r in results)
    )
    clean = {i: _strip_money(item.name) for i, item in enumerate(items)}
    pending = [i for i, item in enumerate(items) if item.parent is None and results[i] is None and clean[i]][:MAX_NAMES]
    if pending and allow_ai and not health_bill and instruction.strip() and gemini.available:
        try:  # GeminiClient tự giới hạn tổng thời gian (8 giây) và khóa mạch khi quá hạn
            answers = await gemini.classify([clean[i] for i in pending], instruction)
        except GeminiFailed:
            answers = None  # món lạ rơi xuống nhóm mặc định / unknown; không bao giờ ném lỗi ra cho client
        if answers is not None:
            ai_used = True
            for i, category in zip(pending, answers):
                if category != "unknown":
                    results[i] = (category, "ai")

    # Bước 4: nhóm mặc định của cửa hàng, chỉ cho món chưa khớp luật và Gemini không quyết được.
    if default:
        for i, item in enumerate(items):
            if item.parent is None and results[i] is None:
                results[i] = (default, "merchant")

    # Bước 1 (làm sau cùng vì cần kết quả CUỐI của món chính) và bước 5.
    out: list[ClassifyItemOut] = []
    for i, item in enumerate(items):
        if item.parent is not None:
            parent = results[item.parent]  # parent < i (schema đã kiểm) nên đã quyết xong
            results[i] = (parent[0], "inherit") if parent else None
        slug, source = results[i] or (None, "unknown")
        out.append(ClassifyItemOut(index=i, category_slug=slug, source=source))
    return out, ai_used
