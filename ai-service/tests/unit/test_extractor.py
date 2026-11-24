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
    assert outcome.matched_pattern_name == "mb_qr_v2"
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


_ND = "TK 1234 GD: -1,000,000VND luc 29-10-2026 09:15. SD: 4,915,000VND. ND: "


def test_generic_uses_only_the_narrative_after_the_label(now):
    outcome = extract_generic(_ND + "mua quan ao", now)
    assert outcome.amount_cents == 1_000_000
    assert outcome.description == "mua quan ao"
    assert outcome.description_is_narrative is True
    assert extract_generic(_ND + "Thanh toan ca phe", now).description == "Thanh toan ca phe"


def test_generic_without_label_keeps_whole_text_and_flag_false(now):
    outcome = extract_generic("Ban da chi 250,000 VND tai Highlands", now)
    assert outcome.description == "Ban da chi 250,000 VND tai Highlands"
    assert outcome.description_is_narrative is False


def test_pattern_description_is_flagged_narrative(matcher, now):
    outcome = Extractor(matcher).extract(
        "com.mbmobile",
        "TK 0123456789|GD: -75,000VND 09/09/2026 10:28|SD: 2,500,000VND|ND: TT QR HIGHLANDS COFFEE",
        now,
        "MB Bank",
    )
    assert outcome.matched_pattern_name is not None
    assert outcome.description == "TT QR HIGHLANDS COFFEE"
    assert outcome.description_is_narrative is True


def test_pattern_with_description_field_keeps_it_and_flags_narrative(now):
    from types import SimpleNamespace

    from app.pipeline.stages.extractor import _from_pattern

    matched = SimpleNamespace(
        amount_cents=5_000, transaction_type=TransactionType.DEBIT, merchant_name=None,
        description="mua tra sua", occurred_at_raw=None, balance_after_cents=None,
        pattern_name="p", pattern_id=None, extra_fields=None,
    )
    outcome = _from_pattern(matched, "so du 9,999,999 VND mua tra sua", now)
    assert outcome.description == "mua tra sua"
    assert outcome.description_is_narrative is True


def _pattern_without_description():
    from types import SimpleNamespace

    return SimpleNamespace(
        amount_cents=4_915_000, transaction_type=TransactionType.DEBIT, merchant_name=None,
        description=None, occurred_at_raw=None, balance_after_cents=None,
        pattern_name="p", pattern_id=None, extra_fields=None,
    )


def test_pattern_without_description_group_falls_back_to_the_nd_label(now):
    from app.pipeline.stages.extractor import _from_pattern

    outcome = _from_pattern(_pattern_without_description(), "TK 01|GD: -4,915,000VND|SD: 4,915,000VND. ND: mua quan ao", now)
    assert outcome.description == "mua quan ao"
    assert outcome.description_is_narrative is True


async def test_pattern_without_label_is_not_narrative_and_never_reaches_gemini(now):
    from app.core.enums import ExtractionMethod
    from app.pipeline.stages.ai_categorizer import AiDeps, categorize_with_ai
    from app.pipeline.stages.categorizer import CategorizationOutcome
    from app.pipeline.stages.extractor import _from_pattern
    from app.utils.text_utils import normalize_whitespace
    from tests.unit.test_item_classifier import FakeGemini, FakeKnowledge

    body = "TK 01|GD: -4,915,000VND  SD: 4,915,000VND tai cua hang"
    outcome = _from_pattern(_pattern_without_description(), body, now)
    assert outcome.description == normalize_whitespace(body)[:255]
    assert outcome.description_is_narrative is False
    gemini = FakeGemini(lambda n: "shopping")
    rule = CategorizationOutcome("other", 0.3, ExtractionMethod.RULE)
    got = await categorize_with_ai(rule, outcome, True, AiDeps(gemini, FakeKnowledge(), "LUAT"), timeout=1.0)
    assert got == rule and gemini.calls == []


def test_generic_direction_follows_the_sign_stuck_to_the_amount(now):
    """Ngân hàng chưa có pattern: dấu +/- sát số tiền quyết định chiều tiền, thắng cả động từ đoán được."""
    credit = extract_generic("Thời gian giao dịch: 01:30 28/12/2025.\nSố tiền GD: +1 VND\nSố dư cuối: 1 VND", now)
    debit = extract_generic("(TPBank): 25/12/25;08:15\nPS:-50,000VND\nSD: 930VND\nND: nhan hang", now)
    assert credit.transaction_type is TransactionType.CREDIT
    assert debit.transaction_type is TransactionType.DEBIT  # "nhan" trong nội dung không đảo được dấu trừ


def test_generic_sign_ignores_a_hyphen_inside_a_date_or_code(now):
    outcome = extract_generic("Ban da nhan 500.000d ngay 12-10-2026 ma A-5", now)
    assert outcome.transaction_type is TransactionType.CREDIT  # không có dấu dính liền số tiền: rơi về động từ


# ---- VCB: thông báo thật 23-11-2026 (mẫu người dùng báo lỗi: ra -10.000 thay vì +5.000) ----
VCB_REAL = (
    "Số dư TK VCB 9982815244 +5,000 VND lúc 23-11-2026 03:53:11. Số dư 10,000 VND. "
    "Ref 158245989977-LE MINH TUAN chuyen tien qua MoMo-CHUYEN TIEN-OQCH000P3Sby-"
    "MOMO158245989977MOMO#SP#0200970422112303531020260AB6915637.5389.22283.035311"
)


def _vcb(matcher, now, body):
    from app.schemas.request import AnalyzeRequest  # đi đúng đường vào của pipeline (chuẩn hóa NFC)

    request = AnalyzeRequest(
        backend_request_id="00000000-0000-0000-0000-000000000001", user_id_hash="a" * 64,
        package_name="com.VCB", notification_body=body, received_at=now,
    )
    return Extractor(matcher).extract("com.VCB", request.notification_body, now)


def test_vcb_real_notification_splits_amount_balance_counterparty_and_note(matcher, now):
    outcome = _vcb(matcher, now, VCB_REAL)
    assert (outcome.amount_cents, outcome.balance_after_cents) == (5_000, 10_000)
    assert outcome.transaction_type == TransactionType.CREDIT
    assert outcome.merchant_name == "LE MINH TUAN"
    assert outcome.description == "CHUYEN TIEN"
    assert outcome.transacted_at == datetime(2026, 11, 23, 3, 53, 11, tzinfo=VIETNAM_TZ)
    assert outcome.matched_pattern_name == "vcb_v2_ref_narrative"


def test_vcb_decomposed_unicode_still_reads_amount_not_balance(now):
    """Chữ dấu rời làm trượt nhãn "số dư" → số dư 10.000 bị đọc là số tiền, chiều thành chi."""
    import unicodedata

    from app.schemas.request import AnalyzeRequest

    body = unicodedata.normalize("NFD", VCB_REAL)
    request = AnalyzeRequest(
        backend_request_id="00000000-0000-0000-0000-000000000001", user_id_hash="a" * 64,
        package_name="com.x", notification_body=body, received_at=now,
    )
    # nhánh generic (không pattern): đủ chứng minh số dư không còn bị nhận nhầm làm số tiền
    outcome = extract_generic(request.notification_body, now)
    assert (outcome.amount_cents, outcome.balance_after_cents) == (5_000, 10_000)
    assert outcome.transaction_type == TransactionType.CREDIT


def test_vcb_debit_and_credit_variants(matcher, now):
    debit = _vcb(matcher, now, "Số dư TK VCB 0011001234567 -250,000 VND lúc 23-11-2026 10:00:00. Số dư 1,750,000 VND. Ref 987654321-NGUYEN VAN A chuyen tien-TIEN AN")
    assert (debit.amount_cents, debit.balance_after_cents, debit.transaction_type) == (250_000, 1_750_000, TransactionType.DEBIT)
    assert (debit.merchant_name, debit.description) == ("NGUYEN VAN A", "TIEN AN")

    no_ref = _vcb(matcher, now, "Số dư TK VCB 0011001234567 +1,000,000 VND lúc 23-11-2026 10:00:00. Số dư 3,000,000 VND.")
    assert (no_ref.amount_cents, no_ref.balance_after_cents, no_ref.transaction_type) == (1_000_000, 3_000_000, TransactionType.CREDIT)
    assert no_ref.matched_pattern_name == "vcb_v1"

    dotted_ref = _vcb(matcher, now, "Số dư TK VCB 1038646763 +20,000,000 VND lúc 19-07-2026 10:19:38. Số dư 20,550,000 VND. Ref 213085.190724.101936.SW494931287")
    assert (dotted_ref.amount_cents, dotted_ref.balance_after_cents) == (20_000_000, 20_550_000)
    assert dotted_ref.matched_pattern_name == "vcb_v1"


def test_vcb_ref_without_transfer_phrase_keeps_amounts_and_short_note(matcher, now):
    outcome = _vcb(matcher, now, "Số dư TK VCB 0011001234567 +750,000 VND lúc 23-11-2026 09:30:00. Số dư 2,000,000 VND. Ref 55512345-NGUYEN VAN B thanh toan hoc phi-MA GD 0042")
    assert (outcome.amount_cents, outcome.balance_after_cents, outcome.transaction_type) == (750_000, 2_000_000, TransactionType.CREDIT)
    assert outcome.matched_pattern_name == "vcb_v3_ref_free_text"
    assert outcome.description == "NGUYEN VAN B thanh toan hoc phi"


VCB_MBVCB_OUT = (
    "Số dư TK VCB 9982815244 -40,000 VND lúc 23-11-2026 16:16:02. Số dư 00 VND. "
    "Ref MBVCB.17184288636.673829.mua com.CT tu 9982815244 LE MINH TUAN toi 6353866323 LE MINH TUAN tai BIDV"
)


def test_vcb_mbvcb_transfer_out_reads_amount_zero_balance_and_receiver(matcher, now):
    """Thông báo thật 16:16: Ref chứa mã dài có dấu chấm. Trước đây số dư bị đọc thành mã Ref (~1,7e16)."""
    outcome = _vcb(matcher, now, VCB_MBVCB_OUT)
    assert (outcome.amount_cents, outcome.balance_after_cents, outcome.transaction_type) == (40_000, 0, TransactionType.DEBIT)
    assert (outcome.merchant_name, outcome.description) == ("LE MINH TUAN", "mua com")
    assert outcome.matched_pattern_name == "vcb_v4_ref_mbvcb_out"


def test_vcb_mbvcb_transfer_in_uses_the_sender_as_counterparty(matcher, now):
    outcome = _vcb(matcher, now, "Số dư TK VCB 9982815244 +40,000 VND lúc 23-11-2026 16:16:02. Số dư 140,000 VND. Ref MBVCB.1.2.tra no.CT tu 6353866323 NGUYEN VAN A toi 9982815244 LE MINH TUAN tai BIDV")
    assert (outcome.amount_cents, outcome.balance_after_cents, outcome.transaction_type) == (40_000, 140_000, TransactionType.CREDIT)
    assert outcome.merchant_name == "NGUYEN VAN A"


def test_generic_branch_never_reads_a_reference_number_as_the_balance(now):
    """Không pattern nào khớp: nhãn "số dư" không được vượt qua ranh giới câu để dính vào mã Ref phía sau."""
    outcome = extract_generic("TK 99 -40,000 VND. So du 00 VND. Ref ABC.17184288636.673829 chuyen khoan", now)
    assert outcome.amount_cents == 40_000
    assert outcome.balance_after_cents is None or outcome.balance_after_cents < 10**12
