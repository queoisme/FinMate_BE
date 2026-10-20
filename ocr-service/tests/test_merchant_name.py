import sys, pathlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

import pytest

from classification.brands import BRAND_CATEGORIES, BRAND_DISPLAY
from merchant_name import format_merchant_name

CASES = {
    "BACHHóA XANH": "Bách Hóa Xanh",
    "PHIEU THANH TOAN BACHHóA XANH": "Bách Hóa Xanh",
    "co.cpmart": "Co.opmart",
    "WINMART": "WinMart",
    "WinMart": "WinMart",
    "nhathuoclongchau": "Nhà thuốc Long Châu",
    "Nha thuoc Long Chau": "Nhà thuốc Long Châu",
    "Trung tam mua säm AEON-TAN AN": "AEON",
    "HIGHLANDS COFFEE": "Highlands Coffee",
    "LAM DU'O'NG": "Lam Duong",
    "PizzaHut": "Pizza Hut",
    "  quan   com  tam!! ": "Quan Com Tam",
    "CTY TNHH ABC": "Abc",
    "CONG TY TNHH TM DV XNK THUC.PHAM SACH": "Thuc Pham Sach",
    "CONG TY TNHH TM DV XNK THUC.PHAM SACH ": "Thuc Pham Sach",
    "CÔNG TY TNHH THƯƠNG MẠI DỊCH VỤ XUẤT NHẬP KHẨU THỰC PHẨM SẠCH": "THỰC PHẨM SẠCH".title(),
    "HỘ KINH DOANH COCO MART": "Coco Mart",
    "Cong ty TNHH Highlands Coffee": "Highlands Coffee",
    "CONG TY TNHH": "Cong Ty TNHH",
    "Co Mart": "Co Mart",
    "Cong Tytnhh Thycpham Dino": "Thycpham Dino",                # "Tytnhh" dính chữ được tách rồi mới cắt loại hình
    "iCONGTY TNHH PHAN PHOI HUYNHGIA": "Phan Phoi Huynhgia",     # chữ i thừa ở đầu, CONGTY dính, "phân phối" không bị cắt
    "CONGTY TNHH ABC": "Abc",
    "Tytnhh TM DV Xnk Thuc Phamsach": "Thuc Phamsach",          # bị cắt mất "Cong" từ trước, vẫn phải gọn lại
    "Ty TNHH ABC": "Abc",
    "Ty Phu Tea": "Ty Phu Tea",                                  # "Ty" đầu tên riêng thì giữ
    "CONG TYTNHH TM DV XNK THUC.PHAMSACH": "Thuc Phamsach",
    "Co Phan Sua Vinamilk": "Sua Vinamilk",
    "Phan Thiet Seafood": "Phan Thiet Seafood",                 # "Phan" đầu tên riêng không phải "cổ phần"
    "HOA DON BAN LE QUAN BA SAU": "Quan Ba Sau",
}


@pytest.mark.parametrize("raw,expected", CASES.items())
def test_formats_names_nicely(raw, expected):
    assert format_merchant_name(raw) == expected


@pytest.mark.parametrize("raw", CASES)
def test_formatting_is_idempotent(raw):
    once = format_merchant_name(raw)
    assert format_merchant_name(once) == once


def test_noise_only_returns_empty_string():
    assert format_merchant_name("***") == ""
    assert format_merchant_name("") == ""


def test_every_brand_has_a_display_name():
    assert set(BRAND_CATEGORIES) <= set(BRAND_DISPLAY)
