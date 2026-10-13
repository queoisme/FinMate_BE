"""Chữ tiếng Việt cho chat: bỏ dấu và đọc số viết bằng chữ ("bốn mươi lăm nghìn"). Chép từ bộ đọc bill, chỉ giữ phần chat cần."""
import re
import unicodedata


def fold(text: str) -> str:
    """Chữ thường, bỏ dấu tiếng Việt (đ -> d)."""
    text = unicodedata.normalize("NFD", text.replace("đ", "d").replace("Đ", "D"))
    return "".join(c for c in text if unicodedata.category(c) != "Mn").lower()


_WORD_ALIAS = {"uoi": "muoi", "tra": "tram", "nghn": "nghin"}  # OCR bỏ dấu và rớt chữ: "mươi" -> "uoi", "trăm" -> "tra" (chỉ có hiệu lực trong đoạn đọc số)
_DIGITS = {"mot": 1, "hai": 2, "ba": 3, "bon": 4, "tu": 4, "nam": 5, "lam": 5, "sau": 6, "bay": 7, "tam": 8, "chin": 9}
_ZERO_WORDS = {"khong", "linh", "le"}
_NUMBER_WORDS = set(_DIGITS) | _ZERO_WORDS | {"muoi", "moi", "tram", "nghin", "ngan", "trieu"}


def words_to_amount(text: str) -> int | None:
    """"Tám trăm bảy mươi ba nghìn đồng" -> 873000. Gặp chữ lạ (OCR hỏng) trong đoạn số thì trả None, không đoán."""
    words = re.findall(r"[a-z]+", fold(text))
    start = next((i for i, w in enumerate(words) if w in _NUMBER_WORDS), None)
    if start is None:
        return None
    millions = thousands = cur = pending = n = 0
    last_digit = False
    for w in words[start:]:
        w = _WORD_ALIAS.get(w, w)
        was_digit, last_digit = last_digit, w in _DIGITS
        if w in ("dong", "d"):
            break
        if w in _DIGITS:
            if was_digit and pending >= 2:  # nói tắt hàng chục: "hai lăm" = 25, "ba mốt" = 31
                cur, pending = cur + pending * 10, 0
            pending = _DIGITS[w]
        elif w in _ZERO_WORDS:
            continue
        elif w in ("muoi", "moi"):  # OCR hay mất dấu "mươi" -> "moi"
            cur, pending = cur + (pending or 1) * 10, 0
        elif w == "tram":
            cur, pending = cur + pending * 100, 0
        elif w in ("nghin", "ngan"):
            thousands, cur, pending = cur + pending, 0, 0
        elif w == "trieu":
            millions, cur, pending = cur + pending, 0, 0
        else:
            return None
        n += 1
    return (millions * 1_000_000 + thousands * 1000 + cur + pending) if n >= 2 else None
