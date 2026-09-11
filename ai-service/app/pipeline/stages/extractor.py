"""Stage 2 — bóc số tiền, chiều tiền, merchant, thời điểm, số dư.

Stage này KHÔNG dùng model, có chủ ý. Thông báo ngân hàng là chuỗi template cố định nên
regex vừa chính xác hơn vừa giải thích được: khi số tiền ra sai, ``matched_pattern_name``
chỉ thẳng vào dòng cần sửa. Một model sinh ra số tiền thì không có cách nào truy vết, mà
sai số tiền là loại lỗi đắt nhất trong ứng dụng này.

Hai đường đi:
1. Pattern của provider khớp → độ tin cậy cao.
2. Không pattern nào khớp (ngân hàng lạ, hoặc câu người dùng tự gõ) → nhánh generic:
   tìm cụm số có đơn vị, đoán chiều tiền theo động từ, lấy merchant sau giới từ.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime

from app.core.enums import ExtractionMethod, TransactionType
from app.utils.provider_patterns import PatternMatcher
from app.utils.text_utils import (
    normalize_whitespace,
    parse_datetime,
    select_amounts,
)

_CREDIT_VERBS: tuple[str, ...] = (
    "nhan",
    "nhận",
    "thu ",
    "luong",
    "lương",
    "thuong",
    "thưởng",
    "hoan tien",
    "hoàn tiền",
    "duoc tra",
    "được trả",
    "ban ",
    "bán ",
    "nap tien",
    "nạp tiền",
    "cong tien",
    "cộng tiền",
    "tra luong",
    "trả lương",
)

_DEBIT_VERBS: tuple[str, ...] = (
    "chi ",
    "tieu",
    "tiêu",
    "tra ",
    "trả ",
    "mua",
    "thanh toan",
    "thanh toán",
    "an ",
    "ăn ",
    "uong",
    "uống",
    "dong ",
    "đóng ",
    "nap ",
    "nạp ",
    "do xang",
    "đổ xăng",
    "gui ",
    "gửi ",
    "rut ",
    "rút ",
    "mung ",
    "mừng ",
    "dat ",
    "đặt ",
    "kham",
    "khám",
    "gia han",
    "gia hạn",
    "di ",
    "đi ",
)

# Giới từ dẫn tới tên đơn vị trong câu tự nhiên: "45k ở Phở Thìn", "500k từ Nguyễn Văn A".
_MERCHANT_PREPOSITIONS: tuple[str, ...] = (
    " ở ",
    " o ",
    " tại ",
    " tai ",
    " cho ",
    " từ ",
    " tu ",
    " cua ",
    " của ",
)


@dataclass(frozen=True)
class ExtractionOutcome:
    amount_cents: int
    transaction_type: TransactionType
    merchant_name: str | None
    description: str | None
    transacted_at: datetime | None
    balance_after_cents: int | None
    confidence: float
    method: ExtractionMethod
    matched_pattern_name: str | None


class Extractor:
    def __init__(self, matcher: PatternMatcher) -> None:
        self._matcher = matcher

    def extract(
        self,
        package_name: str,
        notification_body: str,
        received_at: datetime,
        notification_title: str | None = None,
    ) -> ExtractionOutcome | None:
        matched = self._matcher.match(package_name, notification_body)
        if matched is not None:
            return _from_pattern(matched, notification_body, received_at)
        return extract_generic(notification_body, received_at, notification_title)


def _from_pattern(matched, body: str, received_at: datetime) -> ExtractionOutcome:
    transacted_at = parse_datetime(matched.occurred_at_raw, received_at)
    if transacted_at is None:
        # Thông báo không ghi giờ (ví điện tử hay vậy) — lấy lúc nhận. Không bịa ra một
        # mốc khác: lệch thời gian làm giao dịch rơi nhầm chu kỳ ngân sách.
        transacted_at = received_at

    merchant = matched.merchant_name or _merchant_from_description(matched.description)

    # Bóc được càng nhiều trường thì càng chắc là đúng template, không phải khớp may.
    filled = sum(
        1
        for value in (merchant, matched.balance_after_cents, matched.occurred_at_raw)
        if value
    )
    confidence = (0.90, 0.93, 0.96, 0.98)[filled]

    return ExtractionOutcome(
        amount_cents=matched.amount_cents,
        transaction_type=matched.transaction_type or _infer_type(body),
        merchant_name=merchant,
        description=matched.description or normalize_whitespace(body)[:255],
        transacted_at=transacted_at,
        balance_after_cents=matched.balance_after_cents,
        confidence=confidence,
        method=ExtractionMethod.RULE,
        matched_pattern_name=matched.pattern_name,
    )


def extract_generic(
    text: str, received_at: datetime, title: str | None = None
) -> ExtractionOutcome | None:
    """Nhánh cho ngân hàng chưa có pattern và cho câu người dùng tự gõ."""
    normalized = normalize_whitespace(text)
    selection = select_amounts(normalized)
    candidate = selection.amount
    if candidate is None:
        return None

    transacted_at = parse_datetime(normalized, received_at) or received_at
    merchant = _merchant_after_preposition(normalized, candidate.end)

    return ExtractionOutcome(
        amount_cents=candidate.value,
        transaction_type=_infer_type(f"{title or ''} {normalized}"),
        merchant_name=merchant,
        description=normalized[:255],
        transacted_at=transacted_at,
        balance_after_cents=selection.balance.value if selection.balance else None,
        # Thấp hơn hẳn nhánh pattern: đây là suy đoán, và ngưỡng 85% của Flow 1 nên đẩy
        # những ca này sang hộp thoại chọn danh mục thay vì xác nhận một chạm.
        confidence=0.70 if merchant else 0.60,
        method=ExtractionMethod.RULE,
        matched_pattern_name=None,
    )


def _infer_type(text: str) -> TransactionType:
    lowered = f" {text.lower()} "
    credit_hit = next((lowered.index(v) for v in _CREDIT_VERBS if v in lowered), None)
    debit_hit = next((lowered.index(v) for v in _DEBIT_VERBS if v in lowered), None)

    if credit_hit is not None and debit_hit is not None:
        # Cả hai cùng xuất hiện ("nhận lương rồi trả tiền nhà") — động từ đứng trước là
        # hành động chính của câu.
        return (
            TransactionType.CREDIT if credit_hit < debit_hit else TransactionType.DEBIT
        )
    if credit_hit is not None:
        return TransactionType.CREDIT
    # Mặc định là chi: tuyệt đại đa số thông báo biến động số dư là tiền ra, và đoán nhầm
    # một khoản thu thành chi chỉ làm sai báo cáo, còn đoán ngược lại thì ngân sách của
    # người dùng âm thầm rộng ra.
    return TransactionType.DEBIT


def _merchant_after_preposition(text: str, from_index: int) -> str | None:
    tail = text[from_index:]
    lowered = f" {tail.lower()}"
    for preposition in _MERCHANT_PREPOSITIONS:
        position = lowered.find(preposition)
        if position == -1:
            continue
        merchant = tail[position + len(preposition) - 1 :].strip(" .,;:")
        if merchant:
            return merchant[:120]
    return None


def _merchant_from_description(description: str | None) -> str | None:
    """VNPay/VCB chỉ có trường nội dung — gỡ các tiền tố nghiệp vụ để lộ tên đơn vị."""
    if not description:
        return None
    cleaned = normalize_whitespace(description)
    for prefix in (
        "TT QR",
        "THANH TOAN QR",
        "THANH TOAN",
        "Thanh toan",
        "Thanh toán",
        "CHUYEN TIEN",
        "CHUYEN KHOAN",
        "TT ",
        "TTOAN",
    ):
        if cleaned.upper().startswith(prefix.upper()):
            cleaned = cleaned[len(prefix) :].strip(" -:")
            break
    return cleaned[:120] or None
