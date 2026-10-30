"""Từ vựng đọc thông báo ngân hàng: động từ thu/chi, giới từ dẫn tên đơn vị, tiền tố bỏ đi khỏi tên đơn vị, cụm báo một con số là SỐ DƯ. Sống trong DB (tài liệu ``notification_vocab``), tệp JSON chỉ là seed.

Mọi từ khớp ở ĐẦU và CUỐI từ (không khớp giữa từ: "an" không trúng "nhan"), không phân biệt hoa thường. Nhóm có ý nghĩa thứ tự (giới từ) giữ nguyên thứ tự khai báo.
"""
from __future__ import annotations

import json
import pathlib
import re
from dataclasses import dataclass

_PATH = pathlib.Path(__file__).resolve().parent.parent / "data" / "notification_vocab.json"
GROUPS = ("credit_verbs", "debit_verbs", "merchant_prepositions", "merchant_prefixes", "balance_markers")
MAX_MERCHANT_CHARS = 120
_STRIP_PREFIX = " -:"
_STRIP_MERCHANT = " .,;:"


def default_data() -> dict[str, list[str]]:
    return json.loads(_PATH.read_text(encoding="utf-8"))


def with_defaults(content: dict) -> dict:
    """Bổ sung nhóm mà code thêm SAU khi tài liệu được lưu vào DB (``Spec.upgrade``); nhóm đã có không bị đụng."""
    return {**{group: terms for group, terms in default_data().items() if group not in content}, **content}


def _any_word(terms: list[str]) -> re.Pattern[str] | None:
    """Một regex khớp bất kỳ từ nào ở ranh giới từ; từ dài trước để "thanh toan qr" thắng "thanh toan"."""
    if not terms:
        return None
    ordered = sorted(set(terms), key=lambda term: (-len(term), term))
    return re.compile(r"(?<!\w)(?:" + "|".join(re.escape(term) for term in ordered) + r")(?!\w)", re.IGNORECASE)


@dataclass(frozen=True)
class _Vocab:
    credit: re.Pattern[str] | None
    debit: re.Pattern[str] | None
    prepositions: tuple[re.Pattern[str], ...]
    prefix: re.Pattern[str] | None
    balance: re.Pattern[str] | None


def build(content: dict) -> _Vocab:
    prefix = _any_word(content["merchant_prefixes"])
    return _Vocab(
        credit=_any_word(content["credit_verbs"]),
        debit=_any_word(content["debit_verbs"]),
        prepositions=tuple(re.compile(rf"(?<!\w){re.escape(term)}(?!\w)", re.IGNORECASE) for term in content["merchant_prepositions"]),
        prefix=re.compile(r"^" + prefix.pattern, re.IGNORECASE) if prefix else None,
        balance=_any_word(content["balance_markers"]),
    )


_data = default_data()
_vocab = build(_data)


def apply(content: dict) -> None:
    """Đổi từ vựng đang chạy trong tiến trình; dựng xong rồi mới gán."""
    global _data, _vocab
    vocab = build(content)
    _data, _vocab = content, vocab


def current_data() -> dict[str, list[str]]:
    return _data


def credit_position(text: str) -> int | None:
    """Vị trí động từ thu SỚM NHẤT trong câu; None nếu không có."""
    found = _vocab.credit.search(text) if _vocab.credit else None
    return found.start() if found else None


def debit_position(text: str) -> int | None:
    found = _vocab.debit.search(text) if _vocab.debit else None
    return found.start() if found else None


def merchant_after_preposition(tail: str) -> str | None:
    """Phần sau giới từ đầu tiên (theo thứ tự khai báo) có trong ``tail``; None nếu không giới từ nào có hoặc phần sau rỗng."""
    for pattern in _vocab.prepositions:
        found = pattern.search(tail)
        if found is None:
            continue
        merchant = tail[found.end() :].strip(_STRIP_MERCHANT)
        if merchant:
            return merchant[:MAX_MERCHANT_CHARS]
    return None


def strip_merchant_prefix(description: str) -> str:
    """Bỏ tiền tố nghiệp vụ ở đầu ("TT QR", "THANH TOAN") để lộ tên đơn vị; không có tiền tố thì giữ nguyên."""
    found = _vocab.prefix.match(description) if _vocab.prefix else None
    return description[found.end() :].strip(_STRIP_PREFIX) if found else description


def has_balance_marker(prefix: str) -> bool:
    """``prefix`` (đoạn chữ ngay trước một con số) có cụm báo con số đó là số dư không."""
    return _vocab.balance is not None and _vocab.balance.search(prefix) is not None
