import sys, pathlib
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

import pytest

from memory import MerchantMemory


def test_set_then_get_persists_across_instances(tmp_path):
    path = tmp_path / "m.json"
    MerchantMemory(path).set("Kisnac", "groceries")
    assert MerchantMemory(path).get("Kisnac") == "groceries"


def test_key_ignores_case_diacritics_and_spacing(tmp_path):
    m = MerchantMemory(tmp_path / "m.json")
    m.set("  HIGHLANDS   Cà Phê ", "dining")
    assert m.get("highlands ca phe") == "dining"


def test_fuzzy_match_tolerates_ocr_typos_in_a_garbled_logo(tmp_path):
    m = MerchantMemory(tmp_path / "m.json")
    m.set("FPTAvtail", "healthcare")
    assert m.get("FPTRvtail") == "healthcare"      # lệch 1 ký tự
    assert m.get("Magi") is None                   # khác hẳn
    m.set("ABC", "dining")
    assert m.get("ABD") is None                    # khóa ngắn chỉ khớp chính xác


def test_canonical_display_name_is_remembered_and_found_fuzzily(tmp_path):
    m = MerchantMemory(tmp_path / "m.json")
    m.set("Thực Phẩm Sạch", "groceries", name="Thực Phẩm Sạch")
    assert m.get("THUC.PHAMSACH") == "groceries"
    assert m.display_name("THUC PHAM SACH") == "Thực Phẩm Sạch"      # OCR mất dấu vẫn ra tên đẹp bạn đã chốt
    assert m.display_name("Cua hang khac") is None


def test_old_entries_without_a_display_name_still_work(tmp_path):
    path = tmp_path / "m.json"
    path.write_text('{"winmart": "groceries"}', encoding="utf-8")   # định dạng cũ: chỉ có danh mục
    m = MerchantMemory(path)
    assert m.get("WinMart") == "groceries"
    assert m.display_name("WinMart") is None
    m.set("Kisnac", "groceries", name="Kisnac")                      # ghi mới không làm hỏng mục cũ
    assert m.get("WinMart") == "groceries" and m.display_name("Kisnac") == "Kisnac"


def test_find_in_lines_locates_a_known_name_on_any_line(tmp_path):
    m = MerchantMemory(tmp_path / "m.json")
    m.set("Thực Phẩm Sạch", "groceries", name="Thực Phẩm Sạch")
    m.set("AEON", "groceries", name="AEON")
    lines = ["PHIEU GIAO NHAN", "NVGHQ9_2", "Chi nhanh Dong Nai", "THUC.PHAMSACH", "Tong 5.000"]
    assert m.find_in_lines(lines) == "THUC.PHAMSACH"
    assert m.find_in_lines(["Giao hang tai Thuc Pham Sach quan 9 thanh pho Ho Chi Minh"]) is None   # câu dài, không phải tên
    assert m.find_in_lines(["AEON", "Mall"]) is None          # tên quá ngắn (< 6 ký tự) không dò, tránh khớp nhầm


def test_find_in_lines_prefers_an_exact_match_and_skips_buyer_and_field_lines(tmp_path):
    m = MerchantMemory(tmp_path / "m.json")
    m.set("Thực Phẩm Sạch", "groceries", name="Thực Phẩm Sạch")
    # "KHAU THUC PHAM SACH" chỉ chứa tên (dòng cuối của tên công ty); dòng khớp chính xác phải thắng dù đứng sau
    lines = ["KHAU THUC PHAM SACH", "CONG TY TNHH TM DV XNK", "THUC.PHAM SACH"]
    assert m.find_in_lines(lines) == "THUC.PHAM SACH"
    assert m.find_in_lines(["KHAU THUC PHAM SACH"]) is None        # dư 5 ký tự: không đủ giống
    m.set("Coco Mart", "groceries", name="Coco Mart")
    assert m.find_in_lines(["Khach hang: TCR - COCO MART", "Ten KH CoCo Mart 0866647689"]) is None   # dòng người mua


def test_unknown_or_none_merchant_returns_none(tmp_path):
    m = MerchantMemory(tmp_path / "m.json")
    assert m.get("Nobody") is None
    assert m.get(None) is None


def test_invalid_category_raises(tmp_path):
    with pytest.raises(ValueError):
        MerchantMemory(tmp_path / "m.json").set("Kisnac", "not-a-category")


def test_blank_merchant_raises(tmp_path):
    with pytest.raises(ValueError):
        MerchantMemory(tmp_path / "m.json").set("   ", "dining")


def test_corrupt_file_is_treated_as_empty(tmp_path):
    path = tmp_path / "m.json"
    path.write_text("{not json", encoding="utf-8")
    m = MerchantMemory(path)
    assert m.get("Kisnac") is None
    m.set("Kisnac", "groceries")
    assert m.get("Kisnac") == "groceries"
