"""Hỏi trước khi mua bằng chat ("Hôm nay mua cái quần 800.000đ được không?"): nhận diện hẹp, giá, tên món, ngày định mua (từ hôm nay trở đi).

"Hôm nay" cố định là thứ Bảy 17/10/2026 15:30 giờ Việt Nam để bảng ngày kiểm được từng ngày cụ thể.
"""

import copy
from contextlib import contextmanager
from datetime import date, datetime

import pytest

from app.services import intent_cues
from app.services import config_documents as docs
from app.services.purchase_parser import PurchaseDraft, parse_purchase
from app.services.transaction_parser import VN, parse_chat

NOW = datetime(2026, 10, 17, 15, 30, tzinfo=VN)
NEVER = "(?!x)x"  # mẫu không bao giờ khớp: tắt một dấu hiệu
NEVER_WHEN = "(?P<today>(?!x)x)(?P<tomorrow>)(?P<after>)(?P<day>)(?P<month>)"  # purchase_when phải giữ đủ nhóm đặt tên mà code đọc


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


def replies():
    return intent_cues.current().replies


def draft(text, now=NOW):
    result = parse_purchase(text, now)
    assert result is not None and result.draft is not None, f"{text!r} -> {result}"
    return result.draft


def test_the_sentence_from_the_design():
    assert draft("Hôm nay mua cái quần 800.000đ được không?") == PurchaseDraft("quần", 800_000, date(2026, 10, 17), None)


@pytest.mark.parametrize(
    ("text", "item", "amount"),
    [
        ("mua cái quần 800k được không", "quần", 800_000),
        ("mua laptop 20 triệu có ổn không", "laptop", 20_000_000),
        ("tôi định mua một chiếc iPhone 15 giá 25 triệu có sao không?", "iPhone 15", 25_000_000),
        ("mua giày 1tr2 đủ tiền không", "giày", 1_200_000),
        ("sắm tủ lạnh 8 triệu được ko", "tủ lạnh", 8_000_000),
        ("có nên mua cái quần 800k không", "quần", 800_000),
        ("mua 800k được không", None, 800_000),  # không nói món: backend dùng nhãn mặc định
    ],
)
def test_item_and_price_are_read(text, item, amount):
    got = draft(text)
    assert (got.item, got.amount_cents, got.purchase_date) == (item, amount, None)


def test_the_category_comes_from_the_item_name_and_stays_empty_when_unknown():
    assert draft("mua laptop 20 triệu được không").category_slug == "shopping"
    assert draft("mua cà phê 45k được không").category_slug == "food"
    assert draft("mua 800k được không").category_slug is None  # không có tên món thì không đoán
    assert draft("mua cái quần 800k được không").category_slug is None  # từ điển chưa biết "quần": để người dùng chọn, không đoán


def test_the_item_label_fits_the_backend_limit():
    assert len(draft("mua " + "áo " * 40 + "800k được không").item) <= 60


@pytest.mark.parametrize(
    ("said", "expected"),
    [
        ("hôm nay", date(2026, 10, 17)),
        ("ngày mai", date(2026, 10, 18)),
        ("mai", date(2026, 10, 18)),
        ("ngày kia", date(2026, 10, 19)),
        ("ngày 20", date(2026, 10, 20)),
        ("ngày 17", date(2026, 10, 17)),  # đúng hôm nay vẫn hợp lệ
        ("ngày 16", date(2026, 11, 16)),  # đã qua 16 tháng này: tháng sau
        ("đợi tới ngày 16", date(2026, 11, 16)),
        ("chờ đến ngày 25", date(2026, 10, 25)),
        ("vào ngày 25/12", date(2026, 12, 25)),
        ("ngày 25 tháng 12", date(2026, 12, 25)),
        ("ngày 1/11/2026", date(2026, 11, 1)),
    ],
)
def test_how_a_purchase_day_is_understood(said, expected):
    got = draft(f"{said} mua laptop 20 triệu được không")
    assert got.purchase_date == expected
    assert (got.item, got.amount_cents) == ("laptop", 20_000_000)  # cụm ngày và số trong đó không lọt vào tên món hay giá


@pytest.mark.parametrize("said", ["ngày 5/10", "ngày 31/2", "ngày 40", "ngày 0"])
def test_a_past_or_impossible_day_asks_back_with_no_draft(said):
    result = parse_purchase(f"{said} mua laptop 20 triệu được không", NOW)
    assert result.draft is None and result.reply == replies()["purchase_bad_date"]


def test_a_day_that_the_coming_month_does_not_have_asks_back():
    ask = lambda day, now: parse_purchase(f"ngày {day} mua laptop 20 triệu được không", now)
    assert ask(30, datetime(2026, 1, 31, 9, 0, tzinfo=VN)).reply == replies()["purchase_bad_date"]  # 30/1 đã qua, tháng sau là tháng 2: không có ngày 30
    assert ask(31, datetime(2026, 11, 20, 9, 0, tzinfo=VN)).reply == replies()["purchase_bad_date"]  # tháng 11 không có ngày 31
    assert ask(28, datetime(2026, 1, 31, 9, 0, tzinfo=VN)).draft.purchase_date == date(2026, 2, 28)


@pytest.mark.parametrize("text", ["hôm qua mua quần 800k được không", "tôi vừa mua cái quần 800k có sao không", "mua rồi 800k ổn không"])
def test_something_already_bought_is_not_evaluated(text):
    result = parse_purchase(text, NOW)
    assert result.draft is None and result.reply == replies()["purchase_past"]


def test_a_price_without_a_unit_asks_for_the_unit():
    result = parse_purchase("mua quần tám trăm được không", NOW)
    assert result.draft is None and result.reply == replies()["purchase_needs_unit"]


def test_a_missing_price_asks_for_the_amount():
    for text in ("mua laptop được không?", "đủ tiền mua laptop không", "ngày mai mua giày ổn không"):
        result = parse_purchase(text, NOW)
        assert result.draft is None and result.needs_amount, text
    parsed = parse_chat("mua laptop được không?", None, now=NOW)
    assert parsed.intent == "needs_amount" and parsed.purchase is None and parsed.reply_text == replies()["needs_amount"]


@pytest.mark.parametrize(
    "text",
    [
        "mua cà phê 45k",  # khoản chi thường: không có cách hỏi
        "hôm nay mua cái quần 800k",
        "ăn phở 45k được không",  # không có động từ mua
        "iphone 20 triệu có đáng mua không",  # hỏi giá trị món đồ, không phải túi tiền
        "nên mua nhà hay thuê nhà",  # xin lời khuyên, không có giá
        "có nên mua vàng không",
        "đặt mục tiêu mua laptop 25 triệu",
        "không mua quần 800k nữa",
        "mua nồi cơm điện 800k",  # bỏ dấu thì "mua nồi" trùng "mua nổi"
        "mua nến thơm 200k",  # bỏ dấu thì "nến" trùng "nên"
        "mua nền gạch 3 triệu",
    ],
)
def test_ordinary_sentences_are_not_purchase_questions(text):
    assert parse_purchase(text, NOW) is None


@pytest.mark.parametrize(
    ("text", "intent"),
    [
        ("mua cà phê 45k", "add_expense"),
        ("mua nến thơm 200k", "add_expense"),
        ("hôm qua mua cái quần 800k", "add_expense"),
        ("đặt mục tiêu mua laptop 25 triệu trước 30/12", "create_goal"),
        ("iphone 20 triệu có đáng mua không", "out_of_scope"),
        ("nên mua nhà hay thuê nhà", "out_of_scope"),
        ("tháng này tôi mua quần hết bao nhiêu được không", "ask_spending"),
    ],
)
def test_other_intents_are_untouched(text, intent):
    assert parse_chat(text, None, now=NOW).intent == intent


def test_parse_chat_returns_the_draft_and_no_entry_even_when_classifier_disagrees():
    nlu = {"intent": "add_expense", "confidence": 0.99, "entities": {"amount": "800.000đ"}}
    for source in (None, nlu):
        parsed = parse_chat("Hôm nay mua cái quần 800.000đ được không?", source, now=NOW)
        assert parsed.intent == "evaluate_purchase" and parsed.entries == () and parsed.entry is None and parsed.goal is None
        assert parsed.purchase == PurchaseDraft("quần", 800_000, date(2026, 10, 17), None)
        assert parsed.reply_text == replies()["purchase"] and parsed.unclear_reason is None


def test_triggers_and_item_filler_come_from_the_vocabulary():
    text = "Hôm nay mua cái quần 800.000đ được không?"
    with edited(lambda doc: doc["patterns"]["purchase_buy"].update(regex=NEVER)):
        assert parse_purchase(text, NOW) is None
    with edited(lambda doc: doc["patterns"]["purchase_ask_strong"].update(regex=NEVER)):
        assert parse_purchase(text, NOW) is None
    with edited(lambda doc: doc["patterns"]["purchase_when"].update(regex=NEVER_WHEN)):
        assert draft(text).purchase_date is None
    with edited(lambda doc: doc["lists"].update(purchase_stopwords=[word for word in doc["lists"]["purchase_stopwords"] if word != "cái"])):
        assert draft(text).item == "cái quần"
    with edited(lambda doc: doc["replies"].update(purchase_past="Đã qua.")):
        assert parse_purchase("hôm qua mua quần 800k được không", NOW).reply == "Đã qua."
    assert draft(text).item == "quần"


def test_a_stored_document_saved_before_this_feature_still_works():
    stored = {section: {k: v for k, v in body.items() if "purchase" not in k} if isinstance(body, dict) else body
              for section, body in intent_cues.default_data().items()}
    assert "purchase_when" not in stored["patterns"]
    merged = intent_cues.with_defaults(stored)  # khóa mới được bổ sung từ bản mặc định
    assert intent_cues.build(merged, "test").patterns["purchase_when"].pattern == intent_cues.CUES.patterns["purchase_when"].pattern


@pytest.mark.parametrize("text", ["nên mua laptop nào dưới 20 triệu", "nên mua gì trên 5 triệu", "có nên mua điện thoại nào tối đa 10 triệu"])
def test_a_which_one_question_with_a_budget_cap_is_not_a_purchase_to_assess(text):
    assert parse_purchase(text, NOW) is None  # số tiền là hạn mức tìm món, không phải giá một khoản định mua


def test_a_real_price_question_with_a_choice_word_still_counts_when_it_asks_about_affordability():
    assert parse_purchase("mua laptop 20 triệu có ổn không", NOW) is not None
