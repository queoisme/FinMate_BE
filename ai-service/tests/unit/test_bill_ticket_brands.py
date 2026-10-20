"""Vexere (nền tảng bán vé xe khách) là nhóm đi lại: phiếu lên xe giấy đọc ra cửa hàng "Vexere" nhưng danh mục từng để trống vì từ điển thương hiệu chưa có."""

from app.bill.rules_classify import classify_by_rules


def test_vexere_is_transport():
    result = classify_by_rules("Vexere", [])
    assert (result.category_slug, result.source) == ("transport", "brand")


def test_vexere_in_any_case_is_transport():
    assert classify_by_rules("VEXERE", []).category_slug == "transport"
    assert classify_by_rules("Vexere.com", []).category_slug == "transport"
