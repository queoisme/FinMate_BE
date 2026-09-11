"""Extractor — nhánh pattern và nhánh generic."""

from datetime import datetime

from app.core.enums import ExtractionMethod, TransactionType
from app.pipeline.stages.extractor import Extractor, extract_generic
from app.utils.text_utils import VIETNAM_TZ


def test_pattern_branch_fills_every_field(matcher, now):
    outcome = Extractor(matcher).extract(
        "com.mbmobile",
        "TK 0123456789|GD: -75,000VND 09/09/2026 10:28|SD: 2,500,000VND|ND: TT QR HIGHLANDS COFFEE",
        now,
        "MB Bank",
    )
    assert outcome.amount_cents == 75_000
    assert outcome.transaction_type == TransactionType.DEBIT
    assert outcome.merchant_name == "HIGHLANDS COFFEE"
    assert outcome.balance_after_cents == 2_500_000
    assert outcome.transacted_at == datetime(2026, 9, 9, 10, 28, tzinfo=VIETNAM_TZ)
    assert outcome.matched_pattern_name == "mb_qr_v1"
    assert outcome.method == ExtractionMethod.RULE
    assert outcome.confidence >= 0.95


def test_vcb_dash_date_is_read_not_defaulted_to_arrival_time(matcher, now):
    """Trước khi parser hiểu dấu gạch, mọi giao dịch VCB im lặng lấy thời điểm NHẬN
    thông báo làm thời điểm giao dịch."""
    outcome = Extractor(matcher).extract(
        "com.VCB",
        "So du TK VCB 0011001234567 -250,000 VND luc 08-09-2026 19:05:00. "
        "So du 1,750,000 VND. Ref MBVCB.987654321. ND THANH TOAN GRAB",
        now,
    )
    assert outcome.transacted_at == datetime(2026, 9, 8, 19, 5, tzinfo=VIETNAM_TZ)
    assert outcome.transacted_at != now


def test_generic_branch_prefers_amount_over_balance(now):
    """Ngân hàng chưa có pattern: số dư lớn hơn số tiền giao dịch nên chọn theo giá trị
    lớn nhất là chọn đúng con số sai."""
    outcome = extract_generic(
        "Quy khach vua thanh toan 320,000 VND. So du 1,000,000 VND", now
    )
    assert outcome.amount_cents == 320_000
    assert outcome.balance_after_cents == 1_000_000
    assert outcome.matched_pattern_name is None
    assert outcome.confidence < 0.85


def test_generic_branch_reads_natural_language(now):
    outcome = extract_generic("trưa nay ăn phở 45k ở Phở Thìn", now)
    assert outcome.amount_cents == 45_000
    assert outcome.transaction_type == TransactionType.DEBIT
    assert outcome.merchant_name == "Phở Thìn"


def test_generic_branch_detects_income_verbs(now):
    outcome = extract_generic("nhận lương tháng 9 15tr từ công ty ABC", now)
    assert outcome.amount_cents == 15_000_000
    assert outcome.transaction_type == TransactionType.CREDIT
    assert outcome.merchant_name == "công ty ABC"


def test_generic_branch_returns_none_without_an_amount(now):
    assert extract_generic("hôm nay trời đẹp", now) is None


def test_generic_confidence_stays_below_the_quick_confirm_threshold(now):
    """Flow 1 bước 5.2 cho xác nhận một chạm khi confidence >= 85%. Nhánh suy đoán không
    được rơi vào vùng đó."""
    outcome = extract_generic("Thanh toan 99,000 VND", now)
    assert outcome.confidence < 0.85


def test_extractor_on_full_corpus(matcher, corpus):
    errors = []
    for row in corpus:
        label = row["label"]
        if not label["is_financial"]:
            continue
        outcome = Extractor(matcher).extract(
            row["package_name"],
            row["body"],
            datetime.fromisoformat(row["received_at"]),
            row["title"],
        )
        if outcome is None:
            errors.append(("không bóc được", row["body"]))
        elif outcome.amount_cents != label["amount_cents"]:
            errors.append(("sai số tiền", row["body"], outcome.amount_cents))
        elif outcome.transaction_type.value != label["transaction_type"]:
            errors.append(("sai chiều tiền", row["body"], outcome.transaction_type))
    assert errors == []
