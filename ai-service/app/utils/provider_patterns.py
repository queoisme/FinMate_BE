"""Khớp thông báo với regex của từng provider và bóc ra các trường giao dịch.

Một pattern là MỘT regex phủ cả template, dùng named group. Tách thành nhiều regex mỗi
trường một cái thì hai template khác nhau của cùng ngân hàng có thể khớp chéo group của
nhau — số tiền lấy từ template A, số dư lấy từ template B — và ghép ra một giao dịch chưa
từng tồn tại.

Named group được hiểu:

``amount``       số tiền giao dịch (bắt buộc, không có thì pattern coi như không khớp)
``sign``         ``-``/``+`` hoặc từ khoá chiều tiền; bỏ qua nếu pattern đã chốt
                 ``transaction_type``
``balance``      số dư sau giao dịch
``merchant``     tên đơn vị thụ hưởng
``occurred_at``  cụm ngày giờ, sẽ đưa qua ``text_utils.parse_datetime``
``description``  nội dung giao dịch
"""

from __future__ import annotations

from dataclasses import dataclass

import regex

from app.core.enums import TransactionType
from app.utils.text_utils import normalize_whitespace, parse_vnd_amount

_FLAGS = regex.IGNORECASE | regex.UNICODE | regex.DOTALL

# Ký hiệu chiều tiền hay gặp trong named group ``sign``.
_SIGN_TO_TYPE: dict[str, TransactionType] = {
    "-": TransactionType.DEBIT,
    "−": TransactionType.DEBIT,
    "gd": TransactionType.DEBIT,
    "tru": TransactionType.DEBIT,
    "trừ": TransactionType.DEBIT,
    "+": TransactionType.CREDIT,
    "cong": TransactionType.CREDIT,
    "cộng": TransactionType.CREDIT,
}


@dataclass(frozen=True)
class PatternSpec:
    """Bản sao bất biến của một dòng ``provider_patterns`` — tách khỏi ORM để matcher
    dùng được cả trong test lẫn script không có DB."""

    provider_key: str
    package_name: str
    pattern_name: str
    regex: str
    transaction_type: str | None = None
    priority: int = 100
    sample_text: str | None = None


@dataclass(frozen=True)
class PatternMatch:
    pattern_name: str
    transaction_type: TransactionType | None
    amount_cents: int
    balance_after_cents: int | None
    merchant_name: str | None
    occurred_at_raw: str | None
    description: str | None


class PatternMatcher:
    """Gom pattern theo ``package_name``, compile một lần rồi dùng lại."""

    def __init__(self, specs: list[PatternSpec]) -> None:
        self._by_package: dict[str, list[tuple[PatternSpec, regex.Pattern[str]]]] = {}
        for spec in sorted(specs, key=lambda s: (s.priority, s.pattern_name)):
            compiled = regex.compile(spec.regex, _FLAGS)
            self._by_package.setdefault(spec.package_name, []).append((spec, compiled))

    @property
    def packages(self) -> list[str]:
        return sorted(self._by_package)

    def match(self, package_name: str, text: str) -> PatternMatch | None:
        """Pattern đầu tiên khớp và bóc được số tiền. Không khớp gì thì trả None."""
        candidates = self._by_package.get(package_name, [])
        haystack = normalize_whitespace(text)
        for spec, compiled in candidates:
            found = compiled.search(haystack)
            if found is None:
                continue
            built = _build_match(spec, found)
            if built is not None:
                return built
        return None


def _build_match(spec: PatternSpec, found: regex.Match[str]) -> PatternMatch | None:
    groups = found.groupdict()

    amount = parse_vnd_amount(groups.get("amount"))
    # Regex khớp nhưng số tiền không đọc được nghĩa là pattern sai chứ không phải thông báo
    # lạ — trả None để matcher thử tiếp pattern sau thay vì dựng giao dịch 0 đồng.
    if amount is None or amount <= 0:
        return None

    return PatternMatch(
        pattern_name=spec.pattern_name,
        transaction_type=_resolve_type(spec, groups.get("sign")),
        amount_cents=amount,
        balance_after_cents=parse_vnd_amount(groups.get("balance")),
        merchant_name=_clean(groups.get("merchant")),
        occurred_at_raw=_clean(groups.get("occurred_at")),
        description=_clean(groups.get("description")),
    )


def _resolve_type(spec: PatternSpec, sign: str | None) -> TransactionType | None:
    if spec.transaction_type:
        return TransactionType(spec.transaction_type)
    if not sign:
        return None
    return _SIGN_TO_TYPE.get(sign.strip().lower())


def _clean(value: str | None) -> str | None:
    if value is None:
        return None
    cleaned = normalize_whitespace(value).strip(" .,;:-|")
    return cleaned or None
