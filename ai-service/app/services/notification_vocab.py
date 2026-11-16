"""Từ vựng đọc thông báo ngân hàng: động từ thu/chi, giới từ dẫn tên đơn vị, tiền tố bỏ đi khỏi tên đơn vị, cụm báo một con số là SỐ DƯ, dấu hiệu thông báo có / không phải giao dịch. Sống trong DB (tài liệu ``notification_vocab``), tệp JSON chỉ là seed.

Mọi nhóm khớp NGUYÊN TỪ (không khớp giữa từ: "an" không trúng "nhan"), không phân biệt hoa thường, TRỪ hai nhóm marker phân loại (``financial_markers``, ``non_financial_markers``): chúng khớp chuỗi con trên văn bản viết thường vì có cả đơn vị dính sát số ("75,000VND") và ký hiệu ("gd:"). Nhóm có ý nghĩa thứ tự (giới từ) giữ nguyên thứ tự khai báo.
"""
from __future__ import annotations

import json
import pathlib
import re
from dataclasses import dataclass

_PATH = pathlib.Path(__file__).resolve().parent.parent / "data" / "notification_vocab.json"
GROUPS = (
    "credit_verbs", "debit_verbs", "merchant_prepositions", "merchant_prefixes", "balance_markers", "non_financial_markers", "financial_markers",
    # Phase 81: khoản tiền VÀO là hoàn tiền, đảo giao dịch lỗi, hay cashback khuyến mãi (cashback là thu nhập, không phải hoàn). Khớp nguyên từ.
    "refund_markers", "reversal_markers", "cashback_markers",
    # Phase 83: chuyển tiền giữa các ví của chính người dùng. Nhóm loại trừ (nạp điện thoại, VETC, hoá đơn…) là THANH TOÁN dù có chữ "nạp".
    "transfer_exclusion_markers", "transfer_atm_markers", "transfer_atm_deposit_markers", "transfer_topup_markers",
    "transfer_withdrawal_markers", "transfer_own_account_markers",
)
RELATION_GROUPS = ("cashback_markers", "reversal_markers", "refund_markers")  # thứ tự ưu tiên khi một câu trúng nhiều nhóm
# Nhóm chuyển ví → transfer_kind, và chiều tiền nó cho phép (None = cả hai). Cụm DÀI NHẤT trên mọi nhóm quyết định loại.
TRANSFER_KINDS = {
    "transfer_atm_markers": ("atm_withdrawal", "debit"),
    "transfer_atm_deposit_markers": ("atm_deposit", "credit"),
    "transfer_topup_markers": ("wallet_topup", None),
    "transfer_withdrawal_markers": ("wallet_withdrawal", None),
    "transfer_own_account_markers": ("own_account", None),
}
# Mọi nhóm mà một cụm chỉ được thuộc về đúng một: tiền vào đặc biệt (Phase 81), loại trừ chuyển ví và các loại chuyển ví (Phase 83).
EXCLUSIVE_GROUPS = (*RELATION_GROUPS, "transfer_exclusion_markers", *TRANSFER_KINDS)
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
    non_financial: tuple[str, ...]
    financial: tuple[str, ...]
    cashback: re.Pattern[str] | None
    reversal: re.Pattern[str] | None
    refund: re.Pattern[str] | None
    transfer_exclusion: re.Pattern[str] | None
    transfer: tuple[tuple[re.Pattern[str], str, str | None], ...]


def build(content: dict) -> _Vocab:
    prefix = _any_word(content["merchant_prefixes"])
    return _Vocab(
        credit=_any_word(content["credit_verbs"]),
        debit=_any_word(content["debit_verbs"]),
        prepositions=tuple(re.compile(rf"(?<!\w){re.escape(term)}(?!\w)", re.IGNORECASE) for term in content["merchant_prepositions"]),
        prefix=re.compile(r"^" + prefix.pattern, re.IGNORECASE) if prefix else None,
        balance=_any_word(content["balance_markers"]),
        non_financial=tuple(content["non_financial_markers"]),
        financial=tuple(content["financial_markers"]),
        cashback=_any_word(content["cashback_markers"]),
        reversal=_any_word(content["reversal_markers"]),
        refund=_any_word(content["refund_markers"]),
        transfer_exclusion=_any_word(content["transfer_exclusion_markers"]),
        transfer=tuple(
            (pattern, kind, direction)
            for group, (kind, direction) in TRANSFER_KINDS.items()
            if (pattern := _any_word(content[group])) is not None
        ),
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


def has_non_financial_marker(lowered: str) -> bool:
    """Văn bản (đã viết thường) có dấu hiệu gần như chắc chắn KHÔNG phải giao dịch: OTP, khuyến mãi, đăng nhập..."""
    return any(marker in lowered for marker in _vocab.non_financial)


def financial_signals(lowered: str) -> int:
    """Số dấu hiệu của một biến động số dư thật có trong văn bản (đã viết thường); mỗi dấu hiệu tính một lần dù chồng lên nhau."""
    return sum(1 for marker in _vocab.financial if marker in lowered)


def relation_hint(text: str) -> str | None:
    """``refund`` / ``reversal`` / None cho một khoản tiền vào. Cashback thắng trước ("hoàn tiền khuyến mãi" là thu nhập), rồi tới đảo
    ("hoàn trả GD lỗi" chứa cả chữ hoàn), cuối cùng là hoàn tiền. Chỉ là GỢI Ý: backend không bao giờ tự liên kết theo nó."""
    if _vocab.cashback is not None and _vocab.cashback.search(text):
        return None
    if _vocab.reversal is not None and _vocab.reversal.search(text):
        return "reversal"
    if _vocab.refund is not None and _vocab.refund.search(text):
        return "refund"
    return None


def is_special_money_in(text: str) -> bool:
    """Câu trúng nhóm hoàn tiền, đảo giao dịch hoặc cashback: đó không phải chuyển ví (hoàn tiền nạp ví vẫn là hoàn tiền)."""
    return any(p is not None and p.search(text) for p in (_vocab.cashback, _vocab.reversal, _vocab.refund))


def transfer_kind(text: str, direction: str) -> str | None:
    """
    Phase 83: ``atm_withdrawal`` / ``atm_deposit`` / ``wallet_topup`` / ``wallet_withdrawal`` / ``own_account`` / None. Nhóm loại trừ thắng
    trước (nạp điện thoại là thanh toán); sau đó cụm DÀI NHẤT trên mọi nhóm quyết định loại, nên "rut tien atm" thắng "rut tien". Loại không đi
    được theo chiều tiền này (rút ATM mà lại là tiền vào) thì không có hint. Chỉ là GỢI Ý: backend không bao giờ tự chuyển đổi theo nó.
    """
    if _vocab.transfer_exclusion is not None and _vocab.transfer_exclusion.search(text):
        return None
    best: tuple[int, str, str | None] | None = None
    for pattern, kind, allowed in _vocab.transfer:
        for found in pattern.finditer(text):
            length = found.end() - found.start()
            if best is None or length > best[0]:
                best = (length, kind, allowed)
    if best is None:
        return None
    _, kind, allowed = best
    return kind if allowed is None or allowed == direction else None

