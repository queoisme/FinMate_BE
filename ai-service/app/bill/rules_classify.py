"""Phần LUẬT của phân loại cả hóa đơn (thương hiệu → từ khóa → bình chọn theo món), tách khỏi route để một chỗ duy nhất định nghĩa thứ tự này:
``/bill/classify`` dùng nó, và ô "thử trước khi lưu" của API quản trị dùng đúng nó để cho thấy luật sẽ trả lời gì. Thuần hàm, không I/O."""

from __future__ import annotations

from collections import Counter
from dataclasses import dataclass

from app.bill.classifier import brand_backend_slug, classify_category_detailed
from app.core import confidence as table  # tên khác: hàm dưới có biến cục bộ `confidence`
from app.core.category_map import SLUG_BY_LOCAL_CATEGORY
from app.services.category_resolver import resolve

# Kết quả của luồng hóa đơn luôn cần người xem lại; trần này khớp trần của OCR (remote_ocr.py).
MAX_CONFIDENCE = table.get("bill.max_result")
_BRAND_IN_MERCHANT = table.get("bill.brand_in_merchant")
_PRODUCT_MAJORITY = table.get("bill.product_majority")
_PRODUCT_PLURALITY = table.get("bill.product_plurality")
_ITEM_VOTE_SLUGS = ("family", "education")  # hai nhóm backend mà bộ 9 nhóm của luật cả bill không biểu diễn được


@dataclass(frozen=True)
class RuleResult:
    category_slug: str | None  # slug backend; None khi luật không nhận ra
    local_category: str | None  # nhóm trong 9 nhóm của bộ luật bill (phân biệt dining/groceries mà backend gộp thành food)
    source: str  # brand | keyword | product | none ...
    confidence: float


def item_vote_slug(lines: list[str]) -> tuple[str, float] | None:
    """Bill chỉ toàn đồ em bé / đồ học tập: bỏ phiếu theo luật từng món (``resolve``) để ra ``family`` / ``education``.

    Chỉ nhận khi nhóm thắng là một trong hai nhóm trên và hơn hẳn nhóm đứng sau (hòa thì thôi: bill nhiều nhóm được tách theo món).
    """
    votes = Counter(slug for line in lines if (slug := resolve(line).slug))
    ranked = votes.most_common(2)
    if not ranked or ranked[0][0] not in _ITEM_VOTE_SLUGS:
        return None
    top, n = ranked[0]
    runner_up = ranked[1][1] if len(ranked) > 1 else 0
    if n == runner_up:
        return None
    return top, _PRODUCT_MAJORITY if n >= 2 and n >= 2 * runner_up else _PRODUCT_PLURALITY


def classify_by_rules(merchant: str | None, lines: list[str]) -> RuleResult:
    if brand_slug := brand_backend_slug(merchant):  # thương hiệu có nhóm ngoài 9 nhóm bộ luật (Con Cưng → family): trả thẳng slug backend
        return RuleResult(brand_slug, None, "brand", _BRAND_IN_MERCHANT)
    category, source, confidence = classify_category_detailed(merchant, lines)
    if source == "brand" and confidence < _BRAND_IN_MERCHANT and (slug := brand_backend_slug(" ".join(lines))):
        # thương hiệu chỉ thấy ở thân bill ("App KidsPlaza" ở chân phiếu, website) vì tên cửa hàng đọc sai: cùng ngoại lệ nhóm backend như khi nó nằm ở tên cửa hàng
        return RuleResult(slug, None, "brand", min(confidence, MAX_CONFIDENCE))
    if (source in ("product", "none") or (source == "brand" and confidence < _BRAND_IN_MERCHANT)) and (voted := item_vote_slug(lines)):
        # nhóm cục bộ chỉ đến từ từ khóa sản phẩm / tên thương hiệu của món (bút Thiên Long), không phải từ cửa hàng: theo luật từng món
        return RuleResult(voted[0], None, "product", min(voted[1], MAX_CONFIDENCE))
    known = source != "none"
    return RuleResult(
        SLUG_BY_LOCAL_CATEGORY.get(category) if known else None,
        category if known else None,
        source,
        min(confidence, MAX_CONFIDENCE) if known else 0.0,
    )
