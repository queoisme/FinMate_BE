import io
import sys, pathlib
import unicodedata

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

import pytest
from PIL import Image

import ocr
from text_clean import clean_ocr_text


@pytest.mark.parametrize("raw,expected", [
    ("Chi Nhänh Töng Cöng Ty", "Chi Nhanh Tong Cong Ty"),          # dấu hai chấm kiểu Đức -> chữ gốc
    ("Khäch häng: TCR", "Khach hang: TCR"),
    ("Bün thit nuó'ng", "Bun thit nuó'ng"),                          # ü -> u, giữ nguyên dấu sắc hợp lệ
    ("H6a ddn sδ", "H6a ddn sd"),                                    # chữ Hy Lạp
    ("Straße ø æ å ñ ç", "Strasse o ae a n c"),
    ("48.14§ 70.900", "48.14 70.900"),                               # ký hiệu lạ
])
def test_foreign_diacritics_and_symbols_are_cleaned(raw, expected):
    assert clean_ocr_text(raw) == expected


def test_valid_vietnamese_is_left_exactly_as_is():
    text = "Bách Hóa Xanh – Đường Nguyễn Huệ, Ơ ư ă â ê ô ế ộ ữ ỳ"
    assert clean_ocr_text(text) == text


def test_decomposed_vietnamese_is_recomposed_not_damaged():
    decomposed = unicodedata.normalize("NFD", "Bách Hóa Xanh Đường")
    assert clean_ocr_text(decomposed) == "Bách Hóa Xanh Đường"


def test_cleaning_is_idempotent_and_keeps_digits_and_punctuation():
    raw = "Töng tién: 1.419,282 d (VAT 8%)"
    once = clean_ocr_text(raw)
    assert once == "Tong tién: 1.419,282 d (VAT 8%)"
    assert clean_ocr_text(once) == once


def test_read_lines_returns_cleaned_text(monkeypatch):
    class FakeEngine:
        def ocr(self, img, cls=True):
            return [[([[0, 0], [9, 0], [9, 9], [0, 9]], ("Chi Nhänh Töng Cöng Ty", 0.9))]]

    monkeypatch.setattr(ocr, "_engine", lambda: FakeEngine())
    buf = io.BytesIO()
    Image.new("RGB", (60, 60), "white").save(buf, "PNG")
    assert ocr.read_lines(buf.getvalue()) == [("Chi Nhanh Tong Cong Ty", 0.9)]
