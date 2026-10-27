"""Từ điển tên cửa hàng (tài liệu ``merchant_lexicon``): admin thêm tên cửa hàng thì luồng thông báo ngân hàng và bộ phân loại chung dùng ngay,
bản sai bị chặn trước khi chạm vào chúng, và bản làm tụt kết quả trên bộ đáp án bị phát hiện trong xem trước (luôn trả lại bản đang chạy)."""
import copy

import pytest

from app.bill.rules_admin import RuleError
from app.pipeline.stages import categorizer
from app.services import config_documents as docs
from app.services.category_resolver import resolve

SPEC = docs.spec_of("merchant_lexicon")


@pytest.fixture(autouse=True)
def back_to_default_lexicon():
    yield
    categorizer.apply_lexicon(categorizer.default_lexicon_data())


def lexicon() -> dict:
    return copy.deepcopy(categorizer.default_lexicon_data())


def rejected(content, code):
    with pytest.raises(RuleError) as info:
        SPEC.validate(content)
    assert info.value.status == 422 and info.value.code == code, info.value.message


def test_the_shipped_lexicon_passes_its_own_validation():
    SPEC.validate(lexicon())


# ---------------------------------------------------------------- kiểm tra bản nháp

def test_a_category_cannot_be_removed_or_invented():
    removed = lexicon()
    del removed["food"]
    rejected(removed, "missing_keys")
    invented = lexicon()
    invented["nhom_la"] = ["abc"]
    rejected(invented, "unknown_group")


@pytest.mark.parametrize("word", ["Highlands", "a", " ", "x" * 61])
def test_a_keyword_must_be_lowercase_and_a_sensible_length(word):
    content = lexicon()
    content["food"].append(word)
    rejected(content, "invalid_keyword")


def test_a_trailing_space_that_marks_a_word_boundary_is_kept_valid():
    content = lexicon()
    content["food"].append("zzquan ")
    SPEC.validate(content)


def test_a_keyword_cannot_belong_to_two_categories():
    content = lexicon()
    content["transport"].append(content["food"][0])
    rejected(content, "duplicate_keyword")


def test_the_keywords_must_be_a_list_of_strings():
    content = lexicon()
    content["food"] = "highlands"
    rejected(content, "invalid_keywords")


# ---------------------------------------------------------------- áp dụng nóng

def test_an_activated_merchant_is_recognised_without_a_restart():
    before = categorizer.categorize_by_rules("thanh toan zzquanmoi")
    assert before.category_slug == "other"  # chưa biết cửa hàng này
    content = lexicon()
    content["food"].append("zzquanmoi")
    SPEC.apply(content)
    hit = categorizer.categorize_by_rules("thanh toan zzquanmoi")
    assert hit.category_slug == "food" and hit.confidence >= 0.85  # khớp từ điển đủ chắc để xác nhận một chạm
    assert resolve("zzquanmoi").slug == "food"  # bộ phân loại chung dựng từ điển này cũng được dựng lại
    SPEC.apply(categorizer.default_lexicon_data())
    assert categorizer.categorize_by_rules("thanh toan zzquanmoi").category_slug == "other"
    assert resolve("zzquanmoi").slug is None


def test_the_longest_keyword_still_wins_after_a_change():
    content = lexicon()
    content["bills"].append("zzcoffee house")  # dài hơn "cafe"/"coffee"
    content["food"].append("zzcoffee")
    SPEC.apply(content)
    assert categorizer.categorize_by_rules("zzcoffee house").category_slug == "bills"


def test_a_broken_document_changes_nothing():
    before = categorizer.load_lexicon()
    with pytest.raises(TypeError):
        categorizer.apply_lexicon({"food": None})
    assert categorizer.load_lexicon() is before


# ---------------------------------------------------------------- xem trước

def test_the_preview_of_the_running_lexicon_changes_nothing():
    result = SPEC.preview(lexicon())
    assert result["available"] and result["cases"] > 100
    assert result["delta"] == 0 and result["changed_total"] == 0 and result["keyword_changes"] == {}


def test_the_preview_flags_a_lexicon_that_loses_answers_and_restores_the_running_one():
    content = lexicon()
    content["food"] = ["zzchicoden"]
    before = categorizer.load_lexicon()
    result = SPEC.preview(content)
    assert result["delta"] < 0 and result["changed_total"] > 0
    assert "highlands" in result["keyword_changes"]["food"]["removed"]
    assert categorizer.load_lexicon() == before and categorizer.current_lexicon_data() == lexicon()  # bản đang chạy được trả lại (dựng lại, nên bằng nhau chứ không cùng đối tượng)


def test_the_preview_lists_the_keywords_that_were_added():
    content = lexicon()
    content["food"].append("zzquanmoi")
    assert SPEC.preview(content)["keyword_changes"] == {"food": {"added": ["zzquanmoi"], "removed": []}}
