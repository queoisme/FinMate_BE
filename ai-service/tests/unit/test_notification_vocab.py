"""Tài liệu ``notification_vocab``: từ vựng chiều tiền / tên đơn vị của thông báo ngân hàng sống trong DB, không nằm cứng trong code."""
import pytest

from app.bill.rules_admin import RuleError
from app.core.enums import TransactionType
from app.pipeline.stages.extractor import _infer_type, _merchant_after_preposition, _merchant_from_description
from app.services import config_documents as docs
from app.services import notification_vocab

SPEC = docs.spec_of("notification_vocab")


@pytest.fixture(autouse=True)
def back_to_default_vocab():
    yield
    notification_vocab.apply(notification_vocab.default_data())


def rejected(content):
    with pytest.raises(RuleError) as info:
        SPEC.validate(content)
    return info.value


def test_default_document_is_valid():
    SPEC.validate(notification_vocab.default_data())


@pytest.mark.parametrize(
    "text,expected",
    [
        ("Ban da nhan 500.000d tu A", TransactionType.CREDIT),  # "an" không trúng giữa "nhan"/"ban"
        ("Ban da chi 120.000d tai B", TransactionType.DEBIT),  # "Ban" không dấu không phải động từ "bán"
        ("nhận lương tháng 9 15tr", TransactionType.CREDIT),
        ("trưa nay ăn phở 45k", TransactionType.DEBIT),
        ("chi, 50k", TransactionType.DEBIT),  # dấu câu sau từ vẫn là ranh giới từ
        ("nhận lương rồi trả tiền nhà", TransactionType.CREDIT),  # động từ đứng trước thắng
        ("trả tiền nhà rồi nhận lương", TransactionType.DEBIT),
        ("khong co dong tu nao 45k", TransactionType.DEBIT),  # mặc định là chi
    ],
)
def test_direction_from_the_earliest_verb(text, expected):
    assert _infer_type(text) is expected


def test_apply_changes_direction_without_touching_code():
    assert _infer_type("tien ve 500k") is TransactionType.DEBIT
    notification_vocab.apply({**notification_vocab.default_data(), "credit_verbs": ["tien ve"]})
    assert _infer_type("tien ve 500k") is TransactionType.CREDIT
    assert notification_vocab.current_data()["credit_verbs"] == ["tien ve"]


def test_merchant_after_the_first_declared_preposition():
    text = "45k o Pho Thin, tu Nguyen Van A"
    assert _merchant_after_preposition(text, 3) == "Pho Thin, tu Nguyen Van A"  # "o" khai báo trước "tu"
    assert _merchant_after_preposition("45k nothing here", 3) is None
    assert _merchant_after_preposition("45k cho ", 3) is None  # sau giới từ rỗng


@pytest.mark.parametrize(
    "description,expected",
    [
        ("TT QR HIGHLANDS COFFEE", "HIGHLANDS COFFEE"),
        ("THANH TOAN QR CIRCLE K", "CIRCLE K"),  # tiền tố dài thắng tiền tố ngắn
        ("Thanh toán - Grab", "Grab"),
        ("TTOAN abc", "abc"),  # "tt" không cắt giữa từ "ttoan"
        ("TTX store", "TTX store"),  # không phải tiền tố nguyên từ
        ("HIGHLANDS", "HIGHLANDS"),
    ],
)
def test_merchant_prefix_is_stripped_only_at_a_word_boundary(description, expected):
    assert _merchant_from_description(description) == expected


def test_stored_document_missing_a_new_group_is_filled_from_defaults():
    old = {"credit_verbs": ["nhan"]}
    filled = SPEC.upgrade(old)
    assert filled["credit_verbs"] == ["nhan"]  # nhóm đã có không bị ghi đè
    assert set(filled) == set(notification_vocab.GROUPS)


@pytest.mark.parametrize(
    "mutate,code",
    [
        (lambda c: c.pop("debit_verbs"), "invalid_document"),
        (lambda c: c.update(credit_verbs="nhan"), "invalid_document"),
        (lambda c: c["credit_verbs"].append("NHAN"), "invalid_term"),
        (lambda c: c["credit_verbs"].append("nhan  tien"), "invalid_term"),
        (lambda c: c["credit_verbs"].append("x" * 31), "invalid_term"),
        (lambda c: c["credit_verbs"].append("luong"), "duplicate_term"),
        (lambda c: c["debit_verbs"].append("nhan"), "ambiguous_verb"),
    ],
)
def test_validator_rejects_bad_documents(mutate, code):
    content = notification_vocab.default_data()
    mutate(content)
    assert rejected(content).code == code


VCB = "Số dư TK VCB 1038646763 +20,000,000 VND lúc 19-07-2024 10:19:38. Số dư 20,550,000 VND. Ref 213085.190724"


def test_account_number_after_the_balance_label_is_not_the_balance():
    from app.utils.text_utils import select_amounts

    selection = select_amounts(VCB)
    assert (selection.amount.value, selection.balance.value) == (20_000_000, 20_550_000)  # trước đây số dư = số tài khoản
    assert select_amounts("So du TK 1038646763").balance is None  # dãy số trần dài là mã định danh
    assert select_amounts("So du 500").balance.value == 500  # số nhỏ không dấu vẫn là tiền


def test_balance_markers_come_from_the_document():
    from app.utils.text_utils import select_amounts

    text = "thanh toan 50.000d con du 1.000.000d"
    assert select_amounts(text).balance is None
    notification_vocab.apply({**notification_vocab.default_data(), "balance_markers": ["con du"]})
    assert select_amounts(text).balance.value == 1_000_000


def test_classifier_markers_come_from_the_document():
    from app.core.enums import ClassifierLabel
    from app.pipeline.stages.classifier import classify_by_rules

    promo = "Ưu đãi hoàn tiền 50.000đ cho giao dịch hôm nay"
    assert classify_by_rules(promo, "com.some.bank").label == ClassifierLabel.NON_FINANCIAL
    default = notification_vocab.default_data()
    notification_vocab.apply({**default, "non_financial_markers": [m for m in default["non_financial_markers"] if m not in ("uu dai", "ưu đãi")]})
    assert classify_by_rules(promo, "com.some.bank").label != ClassifierLabel.NON_FINANCIAL  # bỏ marker khỏi tài liệu là đổi hành vi, không cần sửa code


def test_validator_rejects_a_marker_that_points_both_ways():
    content = notification_vocab.default_data()
    content["non_financial_markers"].append("vnd")  # "vnd" đã là dấu hiệu giao dịch
    assert rejected(content).code == "ambiguous_marker"


def test_balance_label_belongs_to_the_number_right_after_it():
    from app.utils.text_utils import select_amounts

    selection = select_amounts("Số dư TK 1038646763 +200,000 VND lúc 10:00 05/10/2026. Số dư 3,400,000 VND")
    assert (selection.amount.value, selection.balance.value) == (200_000, 3_400_000)  # trước đây amount = 2026 (năm trong ngày)
    both = select_amounts("SD: 980VND SD KHA DUNG: 980VND")
    assert both.balance.value == 980 and both.amount is None
