"""Tài liệu ``narrative_labels`` và ``narrative.extract_description``: tách phần 'nội dung' sau nhãn như ND: trong thông báo ngân hàng."""
import pytest

from app.bill.rules_admin import RuleError
from app.services import config_documents as docs
from app.services import narrative

SPEC = docs.spec_of("narrative_labels")


@pytest.fixture(autouse=True)
def back_to_default_labels():
    yield
    narrative.apply(narrative.default_data())


@pytest.mark.parametrize(
    "text,expected",
    [
        ("TK 1234 GD: -1,000,000VND luc 29-10-2026 09:15. SD: 4,915,000VND. ND: mua quan ao", "mua quan ao"),
        ("TK 1234 GD: -1,000,000VND luc 29-10-2026 09:15. SD: 4,915,000VND. ND: Thanh toan ca phe", "Thanh toan ca phe"),
        ("So du 1,750,000 VND. Ref MBVCB.987654321. ND THANH TOAN GRAB", "THANH TOAN GRAB"),
        ("TK 0123|GD:-75,000VND|SD:2,500,000VND|Nội dung: Ăn trưa", "Ăn trưa"),
        ("NGUYEN VAN ND chuyen tien", None),
        ("TK 1 GD: -5,000VND ND:", None),
        ("TK 1 GD: -5,000VND SD: 10,000VND", None),
        ("TK 1. ND: " + "a" * 400, "a" * 255),
        ("TK 1;\n  ND - cafe sang. ND: khac", "cafe sang"),  # ND thứ hai là trường kế tiếp, không thuộc nội dung đầu
        # Mỗi trường một dòng; nội dung bị ngắt dòng vẫn nối lại, và dừng ở trường kế tiếp.
        ("Thời gian giao dịch: 01:30 28/12/2025.\nTài khoản thanh toán: 1\nSố tiền GD: +1 VND\nNội dung giao dịch: Thanh toan lai thang\n12/2025\nMã giao dịch: 0001Lax", "Thanh toan lai thang 12/2025"),
        ("(TPBank): 24/12/25;22:07\nTK: xxxx1\nPS:+7VND\nSD: 980VND\nND: TRA LAI TIEN GUI TK: 3650\nSO GD: 667", "TRA LAI TIEN GUI TK: 3650"),
        # Một dòng: trường kế tiếp bắt đầu sau dấu chấm.
        ("Agribank: 16:17 22/06/2026 Tài khoản 1 +5,000,000VND. Nội dung giao dịch: MB(6)(A B chuyen tien). Số dư cuối: 80,136,010VND", "MB(6)(A B chuyen tien)"),
        ("TK 1. ND: an trua. 12:30 ok", "an trua. 12:30 ok"),  # giờ không phải nhãn trường
    ],
)
def test_extract_description(text, expected):
    assert narrative.extract_description(text) == expected


def test_label_must_be_a_whole_word():
    assert narrative.extract_description("TK 1. NDX: cafe") is None


def test_apply_changes_behaviour():
    text = "TK 1. SD: 1. Ghi chu: cafe"
    assert narrative.extract_description(text) is None
    narrative.apply({"labels": ["ghi chu"]})
    assert narrative.extract_description(text) == "cafe"
    assert narrative.current_data() == {"labels": ["ghi chu"]}


def rejected(content):
    with pytest.raises(RuleError) as info:
        SPEC.validate(content)
    assert info.value.status == 422


def test_default_is_valid():
    SPEC.validate(narrative.default_data())
    assert {"nd", "noi dung", "ndung", "content"} <= set(narrative.default_data()["labels"])


@pytest.mark.parametrize(
    "content",
    [
        {"labels": ["nd", "nd"]},
        {"labels": ["Nd"]},
        {"labels": ["nội dung"]},
        {"labels": ["a"]},
        {"labels": [f"nhan{i}" for i in range(31)]},
        {},
        {"labels": ["nd"], "extra": []},
        {"labels": "nd"},
    ],
)
def test_invalid_documents_are_rejected(content):
    rejected(content)
