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
