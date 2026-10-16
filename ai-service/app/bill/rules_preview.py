"""Thử luật TRƯỚC khi lưu: cho một cửa hàng / dòng chữ / tên món, luật hiện tại trả lời gì và sẽ trả lời gì nếu thêm hoặc tắt các luật đề xuất.

Cách làm: đặt tạm các luật đề xuất vào các bảng trong bộ nhớ, phân loại bằng ĐÚNG hàm của ``/bill/classify`` (``classify_by_rules``), rồi trả bảng về như cũ
trong ``finally``. Cả khối là đồng bộ (không ``await``) nên một request khác không bao giờ thấy bảng ở trạng thái tạm. Không ghi DB. Không log nội dung."""

from __future__ import annotations

from collections.abc import Sequence
from typing import Any

from app.bill import brands, classifier
from app.bill.rules_admin import clean_brand, clean_keyword
from app.bill.rules_classify import classify_by_rules
from app.services import category_resolver
from app.services.category_resolver import resolve


def _targets():
    return (brands.BRAND_CATEGORIES, brands.BRAND_DISPLAY, brands.WHOLE_WORD_BRANDS, classifier._CATEGORY_KEYWORDS, classifier._PRODUCT_KEYWORDS)


def _snapshot():
    return tuple(type(t)(t) for t in _targets())


def _restore(snapshot) -> None:
    for target, value in zip(_targets(), snapshot):
        target.clear()
        target.update(value)
    category_resolver.invalidate()


def _apply(brand_rules: list[tuple[str, str, str, bool, bool]], keyword_rules: list[tuple[str, str, str, bool]]) -> None:
    for key, category, display, whole_word, active in brand_rules:
        if active:
            brands.BRAND_CATEGORIES[key], brands.BRAND_DISPLAY[key] = category, display
            (brands.WHOLE_WORD_BRANDS.add if whole_word else brands.WHOLE_WORD_BRANDS.discard)(key)
        else:
            brands.BRAND_CATEGORIES.pop(key, None)
            brands.BRAND_DISPLAY.pop(key, None)
            brands.WHOLE_WORD_BRANDS.discard(key)
    for kind, category, word, active in keyword_rules:
        table = classifier._CATEGORY_KEYWORDS if kind == "merchant" else classifier._PRODUCT_KEYWORDS
        words = [w for w in table.get(category, ()) if w != word]
        if active:
            words.append(word)
        if words:
            table[category] = tuple(words)
        else:
            table.pop(category, None)
    category_resolver.invalidate()


def _evaluate(merchant: str | None, lines: list[str], item_name: str | None) -> dict[str, Any]:
    bill = None
    if merchant or lines:
        result = classify_by_rules(merchant, lines)
        bill = {"category_slug": result.category_slug, "local_category": result.local_category, "source": result.source, "confidence": result.confidence}
    item = {"rule_slug": resolve(item_name).slug} if item_name else None
    return {"bill": bill, "item": item}


def preview(
    *,
    merchant_name: str | None = None,
    lines: Sequence[str] = (),
    item_name: str | None = None,
    proposed_brands: Sequence[dict] = (),
    proposed_keywords: Sequence[dict] = (),
) -> dict[str, Any]:
    """``proposed_brands``: {brand_key, category_slug, display_name?, whole_word?, active?}; ``proposed_keywords``: {kind, category_slug, keyword, active?}.
    ``active: false`` mô phỏng việc TẮT luật. Đề xuất sai thì ``RuleError`` 422 trước khi đụng tới bảng nào."""
    brand_rules = [(*clean_brand(b.get("brand_key", ""), b.get("category_slug", ""), b.get("display_name", "")), bool(b.get("whole_word", False)), bool(b.get("active", True))) for b in proposed_brands]
    keyword_rules = [(*clean_keyword(k.get("kind", ""), k.get("category_slug", ""), k.get("keyword", "")), bool(k.get("active", True))) for k in proposed_keywords]
    merchant, bill_lines = (merchant_name or None), list(lines)

    current = _evaluate(merchant, bill_lines, item_name)
    if not brand_rules and not keyword_rules:
        return {"current": current, "proposed": None, "changed": False}
    snapshot = _snapshot()
    try:
        _apply(brand_rules, keyword_rules)
        proposed = _evaluate(merchant, bill_lines, item_name)
    finally:
        _restore(snapshot)
    return {"current": current, "proposed": proposed, "changed": proposed != current}
