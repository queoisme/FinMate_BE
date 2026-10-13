"""Đọc số tiền trong câu chat/giọng nói tiếng Việt: "200k", "45 cành", "3 củ 8", "1 lít 2", "3 lít rưỡi", "2 triệu rưỡi", "50.000đ", "một triệu"...

Quy ước tiếng lóng: k/nghìn/ngàn/cành = 1.000; tr/triệu/củ = 1.000.000; lít = 100.000 ("1 lít 2" = 120.000, "3 lít rưỡi" = 350.000);
chữ số ngay sau đơn vị là phần thập phân của đơn vị ("3 củ 8" = 3,8 triệu, "1tr2" = 1,2 triệu); "rưỡi" = +0,5 đơn vị.
Nhiều số trong câu ("mua 2 lít sữa 50k") thì lấy số xuất hiện CUỐI: người ta thường nói giá ở cuối câu.
"""
import re
import unicodedata
from typing import NamedTuple

from app.chat.vi_text import _DIGITS, _NUMBER_WORDS, _WORD_ALIAS, fold, words_to_amount

UNITS = {"k": 1_000, "nghin": 1_000, "ngan": 1_000, "canh": 1_000, "tr": 1_000_000, "trieu": 1_000_000, "cu": 1_000_000, "lit": 100_000}
_NUM = r"\d+(?:[.,]\d+)?"
_SLANG = re.compile(rf"(?<![\w.,])(?P<n>{_NUM})\s*(?P<u>nghin|ngan|canh|trieu|tr|cu|lit|k)(?![a-z])")
_FRAC = re.compile(r"\s*(?P<f>\d{1,2}(?![\d.,])|ruoi(?![a-z]))")
_PLAIN = re.compile(r"(?<![\w.,])(?P<n>\d{1,3}(?:[.,]\d{3})+|\d{4,})(?P<suffix>\s*(?:vnd|dong|d)(?![a-z]))?")
# từ chỉ số lượng đứng ngay sau chữ số: "1 triệu 2 ly cafe" thì 2 là số ly. KHÔNG gồm từ chỉ thời gian ("3 củ 8 tháng này": 8 là phần của số tiền).
_COUNT_WORDS = {"ly", "cai", "nguoi", "lan", "chai", "phan", "to", "do", "bo", "chiec", "hop", "kg", "gam", "tieng", "phut", "qua", "goi"}
# Bỏ dấu thì "từ"="tu", "ngân"="ngan", "sau" (sáu/sau): "rút tiền từ ngân hàng" không phải số tiền. Nên chữ "ngan" không dùng ở đường đọc bằng chữ,
# và đoạn chữ không được bắt đầu bằng "tu"/"lam"; chỉ coi là số tiền khi có từ chỉ bậc (trăm, mươi, nghìn, triệu).
_WORD_VOCAB = (_NUMBER_WORDS - {"ngan"}) | set(_WORD_ALIAS)
_WORDS_RUN = re.compile(rf"(?<![a-z])(?:(?:{'|'.join(sorted(_WORD_VOCAB, key=len, reverse=True))})(?![a-z])\s*)+")
_MAGNITUDE = {"tram", "muoi", "moi", "nghin", "nghn", "trieu"}


class AmountMatch(NamedTuple):
    value: int
    start: int  # vị trí trong câu đã chuẩn hóa (NFC, bỏ dấu): cùng độ dài với câu gốc
    end: int


def _slang(norm: str) -> list[AmountMatch]:
    found = []
    for m in _SLANG.finditer(norm):
        unit = UNITS[m.group("u")]
        value, end = float(m.group("n").replace(",", ".")) * unit, m.end()
        frac = _FRAC.match(norm, end) if m.group("u") != "k" or norm[end:end + 1].isdigit() else None
        if frac:
            nxt = re.match(r"\s*([a-z]+)", norm[frac.end():])
            token = frac.group("f")
            if not (token.isdigit() and nxt and nxt.group(1) in _COUNT_WORDS):  # "1 triệu 2 ly cafe": 2 là số ly, không phải 0,2 triệu
                value += (0.5 if token == "ruoi" else int(token) / 10 ** len(token)) * unit
                end = frac.end()
        found.append(AmountMatch(round(value), m.start(), end))
    for m in re.finditer(r"(?<![a-z])(?P<u>cu|lit|trieu|nghin)\s+ruoi(?![a-z])", norm):  # "củ rưỡi", "lít rưỡi" không có số đứng trước
        if not any(f.start <= m.start() < f.end for f in found):
            found.append(AmountMatch(round(1.5 * UNITS[m.group("u")]), m.start(), m.end()))
    return found


def _plain(norm: str) -> list[AmountMatch]:
    found = []
    for m in _PLAIN.finditer(norm):
        digits = re.sub(r"\D", "", m.group("n"))
        if not m.group("suffix") and len(digits) == 4 and 1900 <= int(digits) <= 2100 and not re.search(r"[.,]", m.group("n")):
            continue  # năm, không phải tiền
        if 1_000 <= int(digits) <= 999_999_999:
            found.append(AmountMatch(int(digits), m.start(), m.end()))
    return found


def _thousand_marked(norm: str, original: str) -> str:
    """"ngàn" (có dấu) là đơn vị tiền; "ngan" không dấu lẫn với "ngân hàng". Đổi riêng "ngàn" thành "nghn" (cùng độ dài, vị trí không lệch)."""
    return re.sub(r"ngan", lambda m: "nghn" if original[m.start():m.end()] == "ngàn" else m.group(0), norm)


def _words(norm: str) -> list[AmountMatch]:
    found = []
    for m in _WORDS_RUN.finditer(norm):
        run = m.group(0).strip()
        words = run.split()
        if words[0] in ("tu", "lam") or not _MAGNITUDE.intersection(words):
            continue
        value = words_to_amount(run)
        if value is None:
            continue
        end = m.end()
        half = re.match(r"\s*ruoi(?![a-z])", norm[end:])  # "một triệu rưỡi", "hai trăm nghìn rưỡi" hiếm nhưng "triệu rưỡi" thường gặp
        bonus = {"trieu": 500_000, "nghin": 500, "nghn": 500, "ngan": 500, "tram": 50}.get(words[-1])  # "hai trăm rưỡi" = 250 (không tự nhân 1000, xem number_words)
        if half and bonus:
            value += bonus
            end += half.end()
        found.append(AmountMatch(value, m.start(), end))
    return found


def parse_amount(text: str) -> AmountMatch | None:
    """Số tiền trong câu (số xuất hiện cuối cùng), hoặc None. start/end tính trên câu đã chuẩn hóa; xem normalize()."""
    norm = normalize(text)
    nfc = unicodedata.normalize("NFC", text).lower()
    candidates = _slang(norm) + _plain(norm)  # số viết bằng chữ số được ưu tiên; chỉ khi không có mới đọc số viết bằng chữ
    if not candidates:
        candidates = _words(_thousand_marked(norm, nfc))
    # bỏ ứng viên nằm trọn trong ứng viên khác (số "1.5" trong "1.5tr" cũng khớp dạng thường)
    candidates = [c for c in candidates if not any(o is not c and o.start <= c.start and c.end <= o.end and (o.end - o.start) > (c.end - c.start) for o in candidates)]
    return max(candidates, key=lambda c: c.start) if candidates else None


def normalize(text: str) -> str:
    """NFC rồi bỏ dấu, chữ thường: cùng độ dài với câu gốc (NFC) nên vị trí khớp dùng lại được cho câu gốc."""
    return fold(unicodedata.normalize("NFC", text))


def remove_amount(text: str, match: AmountMatch) -> str:
    """Câu gốc đã bỏ phần số tiền (và khoảng trắng thừa)."""
    nfc = unicodedata.normalize("NFC", text)
    return re.sub(r"\s+", " ", nfc[:match.start] + " " + nfc[match.end:]).strip(" ,.-")
