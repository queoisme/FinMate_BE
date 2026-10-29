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

import re
from dataclasses import dataclass
from datetime import datetime
from uuid import UUID

from app.core import confidence as table  # tên khác: dataclass và hàm dưới có trường và biến `confidence`
from app.core.enums import ExtractionMethod, TransactionType
from app.services.narrative import extract_description
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
    "bán ",  # dạng không dấu "ban " bị bỏ: nó trùng "Bạn đã…" của thông báo ví và đọc nhầm thành thu
    "nap tien",
    "nạp tiền",
    "cong tien",
    "cộng tiền",
    "tra luong",
    "trả lương",
)

_SIGN_TYPE: dict[str, TransactionType] = {"+": TransactionType.CREDIT, "-": TransactionType.DEBIT, "−": TransactionType.DEBIT}

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

# Động từ chỉ khớp ở ĐẦU từ và lấy cái xuất hiện sớm nhất: khớp giữa từ làm "an " (ăn) trúng "b[an] " và "nh[an] ", nên "Ban da nhan" bị đọc thành chi.
_CREDIT_RE = re.compile(r"(?<!\w)(?:" + "|".join(re.escape(v) for v in _CREDIT_VERBS) + ")")
_DEBIT_RE = re.compile(r"(?<!\w)(?:" + "|".join(re.escape(v) for v in _DEBIT_VERBS) + ")")

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
    pattern_id: UUID | None = None  # dòng kb_provider_patterns đã khớp (None ở nhánh generic)
    extra_fields: dict[str, str | int] | None = None  # trường phụ của định dạng (không có thì None)
    # True: `description` đúng là phần nội dung (nhãn hoặc trường mô tả của định dạng tách ra) — an toàn để gửi AI.
    description_is_narrative: bool = False


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
    confidence = table.get("extractor.pattern_by_filled")[filled]

    if matched.description:
        description, is_narrative = matched.description, True
    else:
        flat = normalize_whitespace(body)
        narrative = extract_description(body)
        description, is_narrative = narrative or flat[:255], narrative is not None

    return ExtractionOutcome(
        amount_cents=matched.amount_cents,
        transaction_type=matched.transaction_type or _infer_type(body),
        merchant_name=merchant,
        description=description,
        transacted_at=transacted_at,
        balance_after_cents=matched.balance_after_cents,
        confidence=confidence,
        method=ExtractionMethod.RULE,
        matched_pattern_name=matched.pattern_name,
        pattern_id=matched.pattern_id,
        extra_fields=matched.extra_fields or None,
        description_is_narrative=is_narrative,
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
    narrative = extract_description(text)

    return ExtractionOutcome(
        amount_cents=candidate.value,
        transaction_type=_signed_type(normalized, candidate.start) or _infer_type(f"{title or ''} {normalized}"),
        merchant_name=merchant,
        description=narrative or normalized[:255],
        transacted_at=transacted_at,
        balance_after_cents=selection.balance.value if selection.balance else None,
        # Thấp hơn hẳn nhánh pattern: đây là suy đoán, và ngưỡng 85% của Flow 1 nên đẩy
        # những ca này sang hộp thoại chọn danh mục thay vì xác nhận một chạm.
        confidence=table.get("extractor.generic_with_merchant" if merchant else "extractor.generic_without_merchant"),
        method=ExtractionMethod.RULE,
        matched_pattern_name=None,
        description_is_narrative=narrative is not None,
    )


def _signed_type(text: str, amount_start: int) -> TransactionType | None:
    """Dấu +/- dính liền số tiền ("GD: -75,000", "PS:+7VND") là tín hiệu chiều tiền chung của gần mọi ngân hàng, đáng tin hơn đoán theo động từ.

    Dấu phải đứng ngay sát chữ số và không dính vào chữ hay số đứng trước ("2025-12", "a-5"), để gạch nối trong ngày tháng hay mã không bị đọc thành dấu trừ.
    """
    for index in (amount_start, amount_start - 1):  # tùy phép khớp số tiền có gồm cả dấu hay không
        if 0 <= index < len(text) and text[index] in _SIGN_TYPE and (index == 0 or not text[index - 1].isalnum()):
            return _SIGN_TYPE[text[index]]
    return None


def _infer_type(text: str) -> TransactionType:
    lowered = f" {text.lower()} "
    credit_hit = match.start() if (match := _CREDIT_RE.search(lowered)) else None
    debit_hit = match.start() if (match := _DEBIT_RE.search(lowered)) else None

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
