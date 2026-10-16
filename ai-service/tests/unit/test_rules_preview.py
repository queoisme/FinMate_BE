"""Ô "thử trước khi lưu" của API quản trị luật: cho thấy luật hiện tại trả lời gì, trả lời gì nếu thêm/tắt luật đề xuất, và KHÔNG để lại dấu vết trong bảng đang chạy."""

import copy

import pytest

from app.bill import brands, classifier
from app.bill.rules_admin import RuleError
from app.bill.rules_preview import preview


def tables():
    return copy.deepcopy((
        brands.BRAND_CATEGORIES, brands.BRAND_DISPLAY, brands.WHOLE_WORD_BRANDS, classifier._CATEGORY_KEYWORDS, classifier._PRODUCT_KEYWORDS,
    ))


def test_current_answer_for_a_known_shop():
    result = preview(merchant_name="Highlands Coffee", lines=["Ca phe sua"])
    assert result["current"]["bill"]["category_slug"] == "food" and result["current"]["bill"]["source"] == "brand"
    assert result["proposed"] is None and result["changed"] is False


def test_a_proposed_product_keyword_changes_an_unknown_bill_and_item():
    result = preview(
        merchant_name="Qwerty Zzz", lines=["zqfoo 10.000", "zqfoo 20.000"], item_name="zqfoo",
        proposed_keywords=[{"kind": "product", "category_slug": "shopping", "keyword": "zqfoo"}],
    )
    assert result["current"]["bill"]["category_slug"] is None and result["current"]["item"]["rule_slug"] is None
    assert result["proposed"]["bill"]["category_slug"] == "shopping" and result["proposed"]["item"]["rule_slug"] == "shopping"
    assert result["changed"] is True


def test_a_proposed_brand_changes_the_shop_and_disabling_it_changes_it_back():
    before = tables()
    added = preview(merchant_name="Zzqq Qwx", lines=[], proposed_brands=[{"brand_key": "Zzqq Qwx", "category_slug": "transport"}])
    assert added["current"]["bill"]["category_slug"] is None and added["proposed"]["bill"]["category_slug"] == "transport"
    assert tables() == before
    try:
        brands.BRAND_CATEGORIES["zzqq qwx"], brands.BRAND_DISPLAY["zzqq qwx"] = "dining", "Zzqq"
        disabled = preview(merchant_name="Zzqq Qwx", lines=[], proposed_brands=[{"brand_key": "zzqq qwx", "category_slug": "dining", "active": False}])
        assert disabled["current"]["bill"]["category_slug"] == "food" and disabled["proposed"]["bill"]["category_slug"] is None
    finally:
        brands.BRAND_CATEGORIES.pop("zzqq qwx", None)
        brands.BRAND_DISPLAY.pop("zzqq qwx", None)


def test_live_tables_are_untouched_after_a_preview_even_when_classification_fails(monkeypatch):
    before = tables()
    preview(merchant_name="Qwerty", lines=["zqfoo"], proposed_keywords=[{"kind": "product", "category_slug": "shopping", "keyword": "zqfoo"}])
    assert tables() == before

    def boom(*args, **kwargs):
        raise RuntimeError("classifier exploded")

    monkeypatch.setattr("app.bill.rules_preview.classify_by_rules", boom)
    with pytest.raises(RuntimeError):
        preview(merchant_name="Qwerty", lines=["zqfoo"], proposed_keywords=[{"kind": "product", "category_slug": "shopping", "keyword": "zqfoo"}])
    assert tables() == before  # try/finally: bảng luôn được trả lại


@pytest.mark.parametrize("proposed", [
    {"proposed_keywords": [{"kind": "chat", "category_slug": "shopping", "keyword": "abc"}]},
    {"proposed_keywords": [{"kind": "product", "category_slug": "income", "keyword": "abc"}]},
    {"proposed_keywords": [{"kind": "product", "category_slug": "shopping", "keyword": "  "}]},
    {"proposed_brands": [{"brand_key": "", "category_slug": "dining"}]},
    {"proposed_brands": [{"brand_key": "x store", "category_slug": "nope"}]},
])
def test_invalid_proposals_are_rejected_before_anything_is_touched(proposed):
    before = tables()
    with pytest.raises(RuleError) as error:
        preview(merchant_name="Qwerty", lines=["zqfoo"], **proposed)
    assert error.value.status == 422 and tables() == before


def test_nothing_to_classify_returns_an_empty_answer():
    result = preview()
    assert result["current"] == {"bill": None, "item": None} and result["changed"] is False
