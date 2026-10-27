"""Từ điển phân loại chat (tài liệu ``chat_dictionary``): admin sửa từ khóa theo danh mục thì chat dùng ngay, bản sai bị chặn trước khi chạm vào chat,
và bản làm tụt kết quả trên bộ câu có đáp án bị phát hiện trong xem trước (luôn trả lại bản đang chạy)."""
import copy
from datetime import datetime

import pytest

from app.bill.rules_admin import RuleError
from app.services import category_resolver
from app.services import config_documents as docs
from app.services import transaction_parser as parser
from app.services import vocab
from app.services.transaction_parser import VN, parse_chat

NOW = datetime(2026, 10, 21, 10, 0, tzinfo=VN)
SPEC = docs.spec_of("chat_dictionary")


@pytest.fixture(autouse=True)
def back_to_default_dictionary():
    yield
    vocab.apply(vocab.default_data())


def dictionary() -> dict:
    return copy.deepcopy(vocab.default_data())


def rejected(content, code):
    with pytest.raises(RuleError) as info:
        SPEC.validate(content)
    assert info.value.status == 422 and info.value.code == code, info.value.message


def chat_category(text):
    entry = parse_chat(text, None, now=NOW).entry
    return docs._shape(entry)[2] if entry else None


def test_the_shipped_dictionaries_pass_their_own_validation():
    SPEC.validate(dictionary())


# ---------------------------------------------------------------- kiểm tra bản nháp

def test_a_group_cannot_be_removed_or_invented():
    removed = dictionary()
    del removed["dining"]
    rejected(removed, "missing_keys")
    invented = dictionary()
    invented["nhom_moi"] = {"kind": "expense", "label": "Nhóm mới", "keywords": ["abc"]}
    rejected(invented, "unknown_group")


def test_a_group_cannot_switch_between_expense_and_income():
    content = dictionary()
    content["salary"]["kind"] = "expense"
    rejected(content, "type_changed")


@pytest.mark.parametrize("word", ["Trà sữa", "trà sữa", "tra  sua", "a", "x" * 61, "TRA SUA"])
def test_a_keyword_must_be_lowercase_unaccented_and_tidy(word):
    content = dictionary()
    content["dining"]["keywords"].append(word)
    rejected(content, "invalid_keyword")


def test_a_keyword_cannot_belong_to_two_groups():
    content = dictionary()
    content["transport"]["keywords"].append(content["dining"]["keywords"][0])
    rejected(content, "duplicate_keyword")


def test_entries_and_labels_must_have_the_right_shape():
    content = dictionary()
    content["dining"]["label"] = "  "
    rejected(content, "invalid_label")
    content = dictionary()
    content["dining"]["keywords"] = "tra sua"
    rejected(content, "invalid_keywords")
    content = dictionary()
    content["dining"]["thua"] = 1
    rejected(content, "invalid_entry")


def test_a_draft_may_add_a_keyword_and_rename_a_label():
    content = dictionary()
    content["dining"]["keywords"].append("zzmonmoi")
    content["dining"]["label"] = "Ăn uống & cà phê"
    SPEC.validate(content)


# ---------------------------------------------------------------- áp dụng nóng

def test_an_activated_keyword_is_used_by_chat_without_a_restart():
    assert chat_category("zzmonmoi 30k") is None  # chưa biết từ này
    content = dictionary()
    content["dining"]["keywords"].append("zzmonmoi")
    SPEC.apply(content)
    assert chat_category("zzmonmoi 30k") == "food"  # từ điển chat nói "dining", quy đổi sang slug backend
    assert category_resolver.resolve("zzmonmoi", chat=True).slug == "food"  # chỉ mục dùng chung với bill cũng được dựng lại
    SPEC.apply(vocab.default_data())
    assert chat_category("zzmonmoi 30k") is None


def test_values_derived_from_the_dictionary_follow_a_renamed_label():
    assert parser.category_from_text("com bui") is None
    content = dictionary()
    content["dining"]["label"] = "Cơm bụi"
    SPEC.apply(content)
    assert parser.category_from_text("com bui") == "dining"  # _SLUG_BY_LABEL được dựng lại, không giữ bản cũ
    assert "dining" in parser.CATEGORIES and "salary" in parser.INCOME_TYPES


def test_a_broken_document_changes_nothing():
    before = (vocab.CHAT_KEYWORDS, vocab.CATEGORY_LABELS, vocab.INCOME_LABELS)
    with pytest.raises(ValueError):
        vocab.apply({"dining": {"kind": "khong_biet", "label": "x", "keywords": ["ab"]}})
    assert (vocab.CHAT_KEYWORDS, vocab.CATEGORY_LABELS, vocab.INCOME_LABELS) == before


# ---------------------------------------------------------------- xem trước

def test_the_preview_of_the_running_dictionary_changes_nothing():
    result = SPEC.preview(dictionary())
    assert result["available"] and result["cases"] > 100
    assert result["delta"] == 0 and result["changed_total"] == 0 and result["keyword_changes"] == {}


def test_the_preview_flags_a_dictionary_that_loses_answers_and_restores_the_running_one():
    content = dictionary()
    content["dining"]["keywords"] = ["zzchicoden"]  # xóa gần hết từ ăn uống
    before = (vocab.CHAT_KEYWORDS, vocab.CATEGORY_LABELS)
    result = SPEC.preview(content)
    assert result["delta"] < 0 and result["changed_total"] > 0
    assert "an com" in result["keyword_changes"]["dining"]["removed"]
    assert (vocab.CHAT_KEYWORDS, vocab.CATEGORY_LABELS) == before and vocab.current_data() == dictionary()  # bản đang chạy được trả lại


def test_the_preview_lists_the_keywords_that_were_added():
    content = dictionary()
    content["dining"]["keywords"].append("zzmonmoi")
    assert SPEC.preview(content)["keyword_changes"] == {"dining": {"added": ["zzmonmoi"], "removed": []}}


# ---------------------------------------------------------------- từ khóa yếu (``weak_keywords``): dữ liệu của tài liệu, không phải tiền tố trong code
# Một từ khóa yếu ("cho con": tiền đến tay ai, không nói mua gì) chỉ quyết danh mục khi câu không có từ khóa thường nào khớp.

RECIPIENTS = {"cho me", "cho bo", "cho ba me", "cho bo me", "cho ong", "cho anh", "cho chi", "cho em", "cho con", "cho vo", "cho chong"}


def test_a_weak_keyword_is_whatever_the_document_says_not_a_cho_prefix():
    content = dictionary()
    content["family"]["weak_keywords"] = ["bieu me"]  # không bắt đầu bằng "cho "
    SPEC.validate(content)
    vocab.apply(content)
    assert category_resolver.resolve("bieu me", chat=True).slug == "family"  # đứng một mình vẫn quyết
    assert category_resolver.resolve("bieu me ca phe", chat=True).slug == "food"  # "ca phe" ngắn hơn nhưng là từ khóa thường nên thắng


def test_a_cho_phrase_not_marked_weak_keeps_full_strength():
    content = dictionary()
    for entry in content.values():  # đưa "cho con" về từ khóa thường, ở bất kỳ danh sách nào đang giữ nó
        entry["keywords"] = [w for w in entry["keywords"] if w != "cho con"]
        entry["weak_keywords"] = [w for w in entry.get("weak_keywords", []) if w != "cho con"]
    content["family"]["keywords"].append("cho con")
    vocab.apply(content)
    # "mua keo" và "cho con" cùng 7 ký tự: hòa thì Gia đình thắng, như mọi từ khóa thường
    assert category_resolver.resolve("cho con mua keo", chat=True).slug == "family"


def test_the_shipped_default_marks_the_recipient_phrases_weak_but_keeps_them_family_keywords():
    assert RECIPIENTS <= vocab.WEAK_KEYWORDS
    assert RECIPIENTS <= set(vocab.CHAT_KEYWORDS["family"])  # mọi nơi khác vẫn thấy chúng là từ khóa Gia đình
    assert parser.category_from_text("chi tieu cho con") == "family"  # câu hỏi chi tiêu cho con còn ra danh mục


def test_weak_keywords_follow_the_same_rules_as_keywords():
    not_a_list = dictionary()
    not_a_list["family"]["weak_keywords"] = "cho con"
    rejected(not_a_list, "invalid_keywords")
    accented = dictionary()
    accented["family"]["weak_keywords"] = ["Cho Con"]
    rejected(accented, "invalid_keyword")
    clash = dictionary()
    clash["family"]["weak_keywords"] = ["an com"]  # đã là từ khóa thường của nhóm khác
    rejected(clash, "duplicate_keyword")
    twice = dictionary()
    twice["family"]["weak_keywords"] = ["bieu me", "bieu me"]
    rejected(twice, "duplicate_keyword")


def test_the_preview_lists_weak_keywords_that_were_added():
    content = dictionary()
    content["family"]["weak_keywords"] = [*content["family"].get("weak_keywords", []), "bieu me"]
    assert SPEC.preview(content)["keyword_changes"] == {"family": {"added": ["bieu me"], "removed": []}}
