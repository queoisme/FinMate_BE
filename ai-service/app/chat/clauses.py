"""Tách một câu chat thành nhiều khoản: "ăn cơm 20k, uống nước 10k và đổ xăng 50k".

Chỉ tách khi CẢ HAI phía của dấu phân cách đều có số tiền và vế sau có chữ mô tả: nhờ vậy dấu phẩy thập phân ("1,5tr"), dấu chấm hàng nghìn,
số lượng ("mua 2 lít sữa 50k"), vế phụ ("ăn cơm 20k, 2 người") và phép cộng ("ăn 20k + 30k") vẫn là MỘT khoản.
"""
from __future__ import annotations

import re

from app.chat.amount import parse_amount, remove_amount

# Dấu phẩy nằm giữa hai chữ số ("1,5tr") không phải dấu phân cách.
_SEPARATOR = re.compile(
    r"(\s*(?<!\d),\s*|\s*,(?!\d)\s*|\s*;\s*|\s+(?:và|va|rồi|roi|sau đó|sau do)\s+|\s*\+\s*)", re.IGNORECASE
)
_WORD = re.compile(r"[^\W\d_]{2,}")


def _is_clause(part: str) -> bool:
    match = parse_amount(part)
    return bool(match) and bool(_WORD.search(remove_amount(part, match)))


def split_clauses(text: str) -> list[str]:
    pieces = _SEPARATOR.split(text)
    parts, separators = pieces[0::2], pieces[1::2]
    clauses = [parts[0]]
    for separator, part in zip(separators, parts[1:]):
        if parse_amount(clauses[-1]) and _is_clause(part):
            clauses.append(part)
        else:
            clauses[-1] += separator + part
    return [clause.strip() for clause in clauses] if len(clauses) > 1 else [text]


# Một vế chỉ gồm lời hủy ("hủy", "thôi", "không mua nữa"): bỏ khoản đứng ngay trước nó. "không cần hủy" hay "hủ tiếu" không khớp vì
# phải là cả vế.
_CANCEL = re.compile(
    r"^\s*(?:à\s+|ờ\s+|ơ\s+)?(?:hủy|huỷ|hoãn|bỏ|thôi|không\s+mua\s+nữa|khỏi)(?:\s+(?:đi|nhé|nha|luôn|khoản\s+đó|cái\s+đó))?\s*$",
    re.IGNORECASE,
)
_CORRECTION = re.compile(r"^\s*(?:à\s+|ờ\s+)?(?:nhầm|sai\s+rồi|nhầm\s+rồi)\s*$", re.IGNORECASE)


def apply_cancellations(text: str) -> tuple[str, bool]:
    """Áp lời hủy và lời sửa trong câu: trả (câu đã bỏ các khoản bị hủy / đã đổi tiền, có áp dụng gì không)."""
    pieces = _SEPARATOR.split(text)
    parts, separators = pieces[0::2], [""] + pieces[1::2]
    kept: list[tuple[str, str]] = []
    changed = correcting = False
    for separator, part in zip(separators, parts):
        if _CANCEL.match(part):
            if kept:
                kept.pop()
            changed = True
        elif _CORRECTION.match(part):
            correcting = True
            changed = True
        elif correcting and (new := parse_amount(part)) and kept:
            earlier_sep, earlier = kept[-1]
            old = parse_amount(earlier)
            if old:
                kept[-1] = (earlier_sep, remove_amount(earlier, old).strip() + " " + part[new.start:new.end])
            correcting = False
        else:
            kept.append((separator, part))
    if not changed:
        return text, False
    return "".join((sep if i else "") + part for i, (sep, part) in enumerate(kept)).strip(), True


# Nhãn tổng đứng riêng như một từ và liền ngay sau là số tiền ("tổng cộng 60k", "tổng là 60k"); "bộ tổng hợp đề 150k" không khớp.
_TOTAL_LABEL = re.compile(r"(?<!\w)(?:tổng\s+cộng|tổng\s+số|tổng|tất\s+cả)(?:\s*[:：]|\s+là|\s+hết)?\s*", re.IGNORECASE)


def extract_total(text: str) -> tuple[str, int | None]:
    """Tách số tiền tổng người dùng nói ra khỏi câu: trả (câu không còn phần tổng, số tiền tổng).

    Chỉ tách khi phần còn lại vẫn có số tiền, để câu chỉ có "tổng 60k" vẫn được coi là một khoản như trước.
    """
    for label in _TOTAL_LABEL.finditer(text):
        amount = parse_amount(text[label.end():])
        if not amount or amount.start > 0:  # số tiền phải liền ngay sau nhãn
            continue
        rest = (text[:label.start()] + text[label.end() + amount.end:]).strip(" ,;.")
        if parse_amount(rest):
            return rest, amount.value
    return text, None


_AND = re.compile(r"\s+(?:và|va|rồi|roi|sau đó|sau do)\s+", re.IGNORECASE)
_FILLER = {"nhé", "nha", "nhá", "luôn", "nữa", "giúp", "với", "đấy", "ạ", "nhỉ"}


def split_amountless(text: str) -> tuple[str, tuple[str, ...]]:
    """Vế cuối nối bằng "và/rồi" mà không có số tiền ("ăn phở 40k và cà phê") không bị gộp vào mô tả: tách ra để báo cho người dùng.

    Chỉ áp khi phần đầu có số tiền; vế phụ ("2 người", bắt đầu bằng chữ số) và lời đệm ("nhé") vẫn nằm trong khoản.
    """
    pieces = _AND.split(text)
    if len(pieces) < 2 or not parse_amount(pieces[0]):
        return text, ()
    kept, skipped = [pieces[0]], []
    for piece in pieces[1:]:
        words = _WORD.findall(piece)
        if parse_amount(piece) or not words or piece.strip()[0].isdigit() or all(w.lower() in _FILLER for w in words):
            kept.append(piece)
        else:
            skipped.append(piece.strip(" ,;."))
    if not skipped:
        return text, ()
    return " và ".join(kept), tuple(skipped)
