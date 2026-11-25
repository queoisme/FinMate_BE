"""Phase 90 (BR-DEBT-12, S3): gợi ý ``debt_repayment`` từ nội dung thông báo, cả hai chiều tiền, có độ mạnh (``strong`` / ``weak``).

Thứ tự: cashback > reversal > refund > transfer > debt_repayment, nhưng giữa bốn nhóm cashback / reversal / refund / debt_repayment cụm DÀI NHẤT
thắng ("hoàn tiền mượn" là trả nợ, không phải hoàn tiền); bằng nhau thì theo thứ tự ưu tiên. Chỉ là GỢI Ý: backend không bao giờ tự liên kết.
"""
import pytest

from app.core.enums import TransactionType
from app.pipeline.stages.extractor import (
    debt_hint_strength_for,
    relation_hint_for,
    transfer_kind_for,
)
from app.services import config_documents as docs
from app.services import notification_vocab

SPEC = docs.spec_of("notification_vocab")
CREDIT = TransactionType.CREDIT
DEBIT = TransactionType.DEBIT


@pytest.fixture(autouse=True)
def back_to_default_vocab():
    yield
    notification_vocab.apply(notification_vocab.default_data())


@pytest.mark.parametrize(
    "text,direction",
    [
        ("TK 0123 +500,000 VND. ND: NAM TRA NO", CREDIT),
        ("+1.000.000đ Lan trả nợ tháng 11", CREDIT),
        ("+300,000 VND HOA TRA TIEN MUON", CREDIT),
        ("+200.000đ hoàn tiền mượn hôm trước", CREDIT),
        ("-3,000,000 VND tra no chi Hoa", DEBIT),
        ("-500.000đ trả tiền mượn anh Tuấn", DEBIT),
    ],
)
def test_strong_markers_give_a_strong_hint_in_both_directions(text, direction):
    assert relation_hint_for(text, direction) == "debt_repayment"
    assert debt_hint_strength_for(text, direction) == "strong"


@pytest.mark.parametrize(
    "text,direction",
    [
        ("+500,000 VND NAM GUI TRA", CREDIT),
        ("+400.000đ Lan gửi trả", CREDIT),
        ("-200,000 VND tra lai tien cho Minh", DEBIT),
    ],
)
def test_weak_markers_give_a_weak_hint(text, direction):
    assert relation_hint_for(text, direction) == "debt_repayment"
    assert debt_hint_strength_for(text, direction) == "weak"


def test_the_longest_phrase_wins_over_refund():
    """ "hoan tien muon" (14 ký tự) chứa "hoan tien" (9 ký tự, nhóm hoàn tiền): cụm dài hơn quyết định."""
    assert relation_hint_for("+200,000 VND hoan tien muon", CREDIT) == "debt_repayment"
    assert relation_hint_for("+900,000 VND hoan tien don hang", CREDIT) == "refund"
    assert relation_hint_for("+50.000đ hoàn tiền khuyến mãi", CREDIT) is None


def test_refund_wins_a_tie_of_equal_length():
    """"coc nhan" (hoàn tiền) và "nhan coc" (trả nợ) cùng 8 ký tự, câu trúng cả hai: thứ tự ưu tiên quyết định."""
    content = notification_vocab.default_data()
    content["refund_markers"] = [*content["refund_markers"], "coc nhan"]
    content["debt_repayment_markers"] = [*content["debt_repayment_markers"], "nhan coc"]
    SPEC.validate(content)
    notification_vocab.apply(content)
    assert relation_hint_for("+500,000 VND coc nhan coc", CREDIT) == "refund"
    assert relation_hint_for("+500,000 VND nhan coc", CREDIT) == "debt_repayment"


def test_transfer_beats_debt_repayment():
    assert transfer_kind_for("-2,000,000 rut tien atm tra no", DEBIT) == "atm_withdrawal"
    assert relation_hint_for("-2,000,000 rut tien atm tra no", DEBIT) == "transfer"


@pytest.mark.parametrize(
    "text",
    [
        "TRA NO KHOAN VAY FE CREDIT",
        "-1,500,000 VND tra no the tin dung VCB",
        "-2,000,000 VND tra gop dien thoai Home Credit",
        "-900,000 VND thanh toan khoan vay HD SAISON tra no",
        "-500,000 VND tra no sao ke thang 10",
    ],
)
def test_bank_loans_and_cards_are_excluded(text):
    assert relation_hint_for(text, DEBIT) is None
    assert debt_hint_strength_for(text, DEBIT) is None


def test_no_marker_no_hint():
    assert relation_hint_for("-500,000 VND chuyen tien cho ban Nam an trua", DEBIT) is None
    assert relation_hint_for("+15,000,000 VND luong thang 10", CREDIT) is None
    assert debt_hint_strength_for("+15,000,000 VND luong thang 10", CREDIT) is None


def test_strength_is_only_for_debt_repayment():
    assert debt_hint_strength_for("+900,000 VND hoan tien don hang", CREDIT) is None
    assert debt_hint_strength_for("-2,000,000 rut tien atm tra no", DEBIT) is None


def test_debt_groups_are_exclusive_with_other_relation_groups():
    content = notification_vocab.default_data()
    content["debt_repayment_weak_markers"] = [*content["debt_repayment_weak_markers"], content["refund_markers"][0]]
    with pytest.raises(Exception) as info:
        SPEC.validate(content)
    assert getattr(info.value, "code", None) == "ambiguous_relation"


def test_a_document_saved_before_phase86_gets_the_new_groups():
    new_groups = ("debt_repayment_markers", "debt_repayment_weak_markers", "debt_repayment_exclusion_markers")
    old = {k: v for k, v in notification_vocab.default_data().items() if k not in new_groups}
    upgraded = SPEC.upgrade(old)
    SPEC.validate(upgraded)
    notification_vocab.apply(upgraded)
    assert relation_hint_for("+500,000 VND NAM TRA NO", CREDIT) == "debt_repayment"
