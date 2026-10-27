"""KidsPlaza và các chuỗi mẹ-và-bé khác là nhóm Gia đình, không phải "mua sắm", kể cả khi tên cửa hàng OCR đọc sai (logo cách điệu) và thương hiệu chỉ nằm ở chân phiếu."""

import pytest

from app.bill.classifier import brand_backend_slug
from app.bill.rules_classify import classify_by_rules


@pytest.mark.parametrize("name", ["KidsPlaza", "Kids Plaza Vincom Thủ Đức", "KIDSPLAZA", "Bibo Mart", "Bibomart", "AVAKids"])
def test_baby_store_chains_are_family(name):
    assert brand_backend_slug(name) == "family"


def test_similar_words_are_not_baby_stores():
    assert brand_backend_slug("Kids Play Cafe") is None
    assert brand_backend_slug("Plaza Hotel") is None


# Ảnh thật: logo "KidsPlaza" cách điệu bị đọc thành "KidcMaza", dòng khẩu hiệu thành tên cửa hàng; chỉ "App KidsPlaza" ở chân phiếu đọc đúng.
SLOGAN = "HE THONG CU'A HANG ME BAU VA EM BE @ANG THIN CAY"
LINES = [SLOGAN, "B KidcMaza", "HÓA ĐƠN GIAO SAU", "Sua PediaSure 85Cg Uc", "Sua chua hoa qua Hoff -vi xoai 4x55g", "Tổng tiền hàng 929,000", "App KidsPlaza"]


def test_brand_only_in_the_footer_still_gives_family():
    result = classify_by_rules(SLOGAN, LINES)
    assert (result.category_slug, result.source) == ("family", "brand")


def test_brand_in_the_store_name_gives_family():
    assert classify_by_rules("KidsPlaza", ["Sua chua Hoff"]).category_slug == "family"


def test_brand_in_a_website_line_gives_family():
    assert classify_by_rules(None, ["Hoa don", "www.kidsplaza.vn"]).category_slug == "family"


def test_other_brands_found_only_in_the_body_are_unchanged():
    result = classify_by_rules("Quan so 1", ["Quan so 1", "Hoa don", "Cam on Highlands Coffee"])
    assert result.category_slug != "family"
