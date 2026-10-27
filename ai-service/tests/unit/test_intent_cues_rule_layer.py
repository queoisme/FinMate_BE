"""Từ vựng của lớp luật hiểu câu chat nằm trong app/data/intent_cues.json, không nằm trong code: bản mặc định giữ đúng hành vi cũ, và sửa tài liệu (bản admin kích hoạt)
đổi được hành vi ngay trong tiến trình, không cần khởi động lại. Mỗi khóa dưới đây có một ca "mặc định" và một ca "sửa tài liệu thì hành vi đổi"."""

import copy
from contextlib import contextmanager
from datetime import datetime

import pytest

from app.bill.rules_admin import RuleError
from app.services import category_resolver, clauses, goal_parser, intent_cues
from app.services import config_documents as docs
from app.services.transaction_parser import VN, _is_social_question, is_question, parse_chat

NOW = datetime(2026, 10, 17, 15, 30, tzinfo=VN)
NEVER = "(?!x)x"  # mẫu không bao giờ khớp: tắt một dấu hiệu


@contextmanager
def edited(change):
    """Kích hoạt (trong tiến trình) một bản từ vựng đã sửa bằng ``change(doc)``, rồi trả về bản đang chạy."""
    doc = copy.deepcopy(intent_cues.default_data())
    change(doc)
    docs.spec_of("intent_cues").validate(doc)
    active = intent_cues.current()
    intent_cues.apply(intent_cues.build(doc, "test"))
    try:
        yield
    finally:
        intent_cues.apply(active)


def set_pattern(name, regex):
    return lambda doc: doc["patterns"][name].update(regex=regex)


# ---------------------------------------------------------------- transaction_parser

def test_description_filler_comes_from_the_vocabulary():
    entry = lambda: parse_chat("tôi ăn phở ở Bà Tư 45k", None, now=NOW).entry
    assert entry().description == "ăn phở ở Bà Tư"
    with edited(lambda doc: doc["lists"].update(description_lead=["và"])):
        assert entry().description == "tôi ăn phở ở Bà Tư"
    assert entry().description == "ăn phở ở Bà Tư"
    trailing = lambda: parse_chat("ăn bún chả hết 55 cành", None, now=NOW).entry.description
    assert trailing() == "ăn bún chả"
    with edited(lambda doc: doc["lists"].update(description_trail=["tốn"])):
        assert trailing() == "ăn bún chả hết"


def test_where_something_was_bought_comes_from_the_vocabulary():
    merchant = lambda text: parse_chat(text, None, now=NOW).entry.merchant
    assert merchant("ăn phở ở Bà Tư 45k") == "Bà Tư" and merchant("ăn phở o nha 45k") is None
    with edited(lambda doc: doc["lists"].update(merchant_prepositions=["tại"])):
        assert merchant("ăn phở ở Bà Tư 45k") is None and merchant("ăn phở tại Bà Tư 45k") == "Bà Tư"
    with edited(lambda doc: doc["lists"].update(not_merchant=["zzz"])):
        assert merchant("ăn phở o nha 45k") == "nha"


def test_question_openers_come_from_the_vocabulary():
    assert is_question("liệu tôi có nên mua", "lieu toi co nen mua") and is_question("nên mua xe", "nen mua xe")
    with edited(lambda doc: doc["lists"].update(question_starts=["có nên"])):
        assert not is_question("liệu tôi có nên mua", "lieu toi co nen mua") and is_question("có nên mua xe", "co nen mua xe")


def test_period_labels_are_editable_but_their_keys_are_fixed():
    label = lambda: parse_chat("tháng này tôi tiêu bao nhiêu", None, now=NOW).query
    assert (label().period_key, label().period_label) == ("thang nay", "tháng này")

    def relabel(doc):
        doc["forms"]["period_labels"]["pairs"][-1][1] = "tháng hiện tại"

    with edited(relabel):
        assert (label().period_key, label().period_label) == ("thang nay", "tháng hiện tại")
        assert parse_chat("tuần trước tôi tiêu bao nhiêu", None, now=NOW).query.period_label == "tuần trước"
    doc = copy.deepcopy(intent_cues.default_data())
    doc["forms"]["period_labels"]["pairs"][0][0] = "hom nay roi"  # code tính khoảng ngày theo khóa nên không được đổi khóa
    with pytest.raises(RuleError) as info:
        docs.spec_of("intent_cues").validate(doc)
    assert info.value.code == "period_keys_changed"


def test_which_model_intents_count_as_a_reply_comes_from_the_vocabulary():
    nlu = {"intent": "deny", "confidence": 0.9}
    assert not _is_social_question("không đúng?", "khong dung", nlu)  # model cho là lời đáp "không": không phải câu hỏi xã giao
    with edited(lambda doc: doc["forms"]["reply_intents"].update(words=["greet", "goodbye"])):
        assert _is_social_question("không đúng?", "khong dung", nlu)


# ---------------------------------------------------------------- clauses

def test_clause_fillers_come_from_the_vocabulary():
    assert clauses.split_amountless("ăn phở 40k và nhé") == ("ăn phở 40k và nhé", ())
    with edited(lambda doc: doc["lists"].update(clause_filler=["ạ"])):
        assert clauses.split_amountless("ăn phở 40k và nhé") == ("ăn phở 40k", ("nhé",))
    assert clauses.split_amountless("ăn phở 40k và về nhà") == ("ăn phở 40k và về nhà", ())
    with edited(lambda doc: doc["lists"].update(not_an_item=["em"])):
        assert clauses.split_amountless("ăn phở 40k và về nhà")[1] == ("về nhà",)


def test_the_clause_separator_comes_from_the_vocabulary():
    assert clauses.split_clauses("ăn cơm 20k và uống nước 10k") == ["ăn cơm 20k", "uống nước 10k"]
    with edited(set_pattern("clause_separator", r"(\s*(?<!\d),\s*)")):
        assert clauses.split_clauses("ăn cơm 20k và uống nước 10k") == ["ăn cơm 20k và uống nước 10k"]
        assert clauses.split_clauses("ăn cơm 20k, uống nước 10k") == ["ăn cơm 20k", "uống nước 10k"]


def test_cancel_and_correction_words_come_from_the_vocabulary():
    assert clauses.apply_cancellations("ăn phở 30k, hủy") == ("", True)
    assert clauses.has_unresolved_cancel("ăn phở 30k hủy")
    assert clauses.apply_cancellations("ăn phở 30k, nhầm, 35k") == ("ăn phở 35k", True)
    with edited(set_pattern("cancel_clause", NEVER)):
        assert clauses.apply_cancellations("ăn phở 30k, hủy") == ("ăn phở 30k, hủy", False)
    with edited(set_pattern("cancel_word", NEVER)):
        assert not clauses.has_unresolved_cancel("ăn phở 30k hủy")
    with edited(set_pattern("correction_clause", NEVER)):
        assert clauses.apply_cancellations("ăn phở 30k, nhầm, 35k")[1] is False


def test_the_stated_total_comes_from_the_vocabulary():
    said = "ăn 20k, uống 40k, tổng cộng 60k"
    assert clauses.extract_total(said) == ("ăn 20k, uống 40k", 60000)
    with edited(set_pattern("total_label", NEVER)):
        assert clauses.extract_total(said) == (said, None)
    breakdown = "trong 100k có 20k ăn cơm và 80k mua sữa"
    assert clauses.extract_total(breakdown) == ("20k ăn cơm và 80k mua sữa", 100000)
    with edited(set_pattern("breakdown_keyword", NEVER)):
        assert clauses.extract_total(breakdown) == (breakdown, None)


# ---------------------------------------------------------------- goal_parser

def goal(text):
    return goal_parser.parse_goal(text, NOW)


def test_goal_triggers_and_past_markers_come_from_the_vocabulary():
    assert goal("đặt mục tiêu tiết kiệm 20 triệu").draft.target == 20_000_000
    assert goal("muốn tiết kiệm 20 triệu để mua xe").draft is not None
    assert goal("đặt mục tiêu tiết kiệm 10 triệu trước hôm qua").draft is None  # hạn đã qua: hỏi lại
    with edited(set_pattern("goal_strong", NEVER)):
        assert goal("đặt mục tiêu tiết kiệm 20 triệu") is None
    with edited(set_pattern("goal_weak", NEVER)):
        assert goal("muốn tiết kiệm 20 triệu để mua xe") is None
    with edited(set_pattern("goal_past", NEVER)):
        assert goal("đặt mục tiêu tiết kiệm 10 triệu trước hôm qua").draft is not None


def test_words_that_are_not_part_of_a_goal_name_come_from_the_vocabulary():
    assert goal("đặt mục tiêu mua laptop 25 triệu nhé").draft.name == "Mua laptop"
    with edited(lambda doc: doc["lists"].update(goal_stopwords=[word for word in doc["lists"]["goal_stopwords"] if word != "nhe"])):
        assert goal("đặt mục tiêu mua laptop 25 triệu nhé").draft.name == "Mua laptop nhé"


# ---------------------------------------------------------------- category_resolver

def test_accent_only_keywords_come_from_the_vocabulary():
    resolve = lambda text: category_resolver.resolve(text, chat=True).slug
    assert resolve("đi chợ") == "food" and resolve("mẹ tôi cho ba triệu") is None
    with edited(lambda doc: doc["forms"]["needs_accent"].update(map={})):
        assert resolve("mẹ tôi cho ba triệu") == "food"  # không còn đòi dấu: "cho" (đưa) bị đọc thành "chợ"
    assert resolve("mẹ tôi cho ba triệu") is None


def test_words_that_name_a_category_not_an_item_come_from_the_vocabulary_and_the_index_follows():
    resolve = lambda text: category_resolver.resolve(text, chat=True).slug
    assert resolve("hàng khác") is None
    with edited(lambda doc: doc["lists"].update(not_item_words=["zzz"])):
        assert resolve("hàng khác") == "other"  # chỉ mục được dựng lại khi tài liệu đổi
    assert resolve("hàng khác") is None


# ---------------------------------------------------------------- mặc định và kiểm tra tài liệu

def test_the_shipped_defaults_for_the_moved_vocabulary():
    cues = intent_cues.CUES
    assert cues.reply_intents == ("greet", "goodbye", "confirm", "deny", "cancel")
    assert [key for key, _ in cues.period_labels] == ["hom nay", "hom qua", "tuan nay", "tuan truoc", "thang truoc", "thang nay"]
    assert cues.needs_accent == {"cho": "chợ", "noi": "nồi", "mua noi": "nồi", "cho chi": "chị"}
    assert cues.lists["not_item_words"] == ("khác",) and "nhé" in cues.lists["clause_filler"]
    assert cues.replies["goal_default_name"] == "Mục tiêu tiết kiệm"


@pytest.mark.parametrize(
    "change",
    [
        lambda doc: doc["forms"]["reply_intents"].update(words=[1]),
        lambda doc: doc["forms"]["needs_accent"].update(map={"cho": 5}),
        lambda doc: doc["forms"]["period_labels"].update(pairs=[["hom nay"]]),
        lambda doc: doc["forms"].pop("needs_accent"),
    ],
)
def test_a_malformed_new_form_is_rejected_before_it_can_break_chat(change):
    doc = copy.deepcopy(intent_cues.default_data())
    change(doc)
    with pytest.raises(RuleError) as info:
        docs.spec_of("intent_cues").validate(doc)
    assert info.value.status == 422
