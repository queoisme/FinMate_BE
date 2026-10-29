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
        ("TK 1;\n  ND - cafe sang. ND: khac", "cafe sang. ND: khac"),
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
