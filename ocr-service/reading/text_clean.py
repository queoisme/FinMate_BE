"""Làm sạch chữ OCR: bỏ các dấu/ký tự của ngôn ngữ khác mà mô hình Latin chung gán nhầm cho tiếng Việt.

Mô hình nhận dạng đang dùng là bộ Latin chung nên hay đọc dấu tiếng Việt thành ä ö ü å ø ß..., hoặc chữ Hy Lạp.
Tiếng Việt không dùng các dấu đó, nên đổi về chữ gốc; dấu tiếng Việt hợp lệ giữ nguyên.
"""
import re
import unicodedata
from classification import data_files

_VN_MARKS = data_files.table("reading.text_clean._VN_MARKS")
_REPLACE = data_files.table("reading.text_clean._REPLACE")
_STRAY_SYMBOLS = re.compile(r"[§¤¶°¦©®™×÷¿¡]")


def clean_ocr_text(text: str) -> str:
    out = []
    for ch in unicodedata.normalize("NFC", text):
        if ch in _REPLACE:
            out.append(_REPLACE[ch])
            continue
        decomposed = unicodedata.normalize("NFD", ch)
        base, marks = decomposed[0], decomposed[1:]
        vietnamese_mark = set(marks) <= _VN_MARKS and base.lower() in "aeiouy"  # dấu ngã trên "n" (ñ) thì không
        if marks and not vietnamese_mark:
            out.append(base)  # dấu không phải của tiếng Việt (ä ö ü å ñ ç...): bỏ dấu
        else:
            out.append(ch)
    return _STRAY_SYMBOLS.sub("", "".join(out))
