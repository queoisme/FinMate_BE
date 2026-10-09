"""Hiểu câu chat: loại giao dịch, số tiền lóng, danh mục, câu hỏi tổng, và khi nào đủ chắc để ghi ngay."""

from datetime import datetime

import pytest

from app.chat.amount import parse_amount
from app.chat.parser import VN, parse_chat, period_range

NOW = datetime(2026, 10, 14, 15, 30, tzinfo=VN)  # thứ Tư


def parse(text, nlu=None, **kw):
    return parse_chat(text, nlu, now=NOW, **kw)


def nlu(intent, confidence=0.99, **entities):
    return {"intent": intent, "confidence": confidence, "entities": entities}


@pytest.mark.parametrize(
    ("text", "amount"),
    [
        ("đi ăn 200k", 200_000),
        ("ăn phở 45 cành", 45_000),
        ("nhận lương 14 củ 5", 14_500_000),
        ("mua sạc dự phòng 1 lít 2", 120_000),
        ("tiền nhà 3 củ 8", 3_800_000),
        ("cafe 50.000đ", 50_000),
        ("ăn trưa bốn mươi lăm nghìn", 45_000),
        ("mua áo 2 triệu rưỡi", 2_500_000),
    ],
)
def test_slang_and_written_amounts(text, amount):
    assert parse_amount(text).value == amount


def test_expense_with_a_known_category_is_auto_saveable():
    entry = parse("đi ăn 200k").entry
    assert (entry.kind, entry.amount, entry.category, entry.auto_save_ok) == ("expense", 200_000, "dining", True)
    assert entry.description == "đi ăn"


def test_the_threshold_is_exclusive_and_configurable():
    assert parse("mua áo 1 triệu 9").entry.auto_save_ok is True
    assert parse("mua áo 2 triệu").entry.auto_save_ok is False
    assert parse("mua áo 3 triệu", auto_save_below=5_000_000).entry.auto_save_ok is True
    assert parse("đi ăn 200k", auto_save_below=0).entry.auto_save_ok is False


def test_unknown_category_is_never_auto_saveable_and_asks_the_caller():
    entry = parse("250k").entry
    assert entry.category is None and entry.auto_save_ok is False


def test_lexicon_fallback_fills_a_category_the_chat_vocabulary_lacks():
    entry = parse("ghé Zzz 100k", fallback_category=lambda merchant, description: "bills").entry
    assert entry.category == "bills" and entry.auto_save_ok is True


def test_income_and_transfer_are_never_auto_saveable():
    salary = parse("nhận lương 14 củ 5").entry
    assert (salary.kind, salary.income_type, salary.category, salary.auto_save_ok) == ("income", "salary", None, False)
    transfer = parse("chuyển 2 triệu sang tiết kiệm").entry
    assert (transfer.kind, transfer.auto_save_ok, transfer.to_others) == ("transfer", False, False)
    assert parse("nạp 300k vào momo").entry.kind == "transfer"
    assert parse("mẹ gửi cho 500k").entry.kind == "income"


def test_transfer_to_another_person_counts_as_an_expense():
    entry = parse("chuyển cho bạn 500k").entry
    assert (entry.kind, entry.category, entry.to_others, entry.auto_save_ok) == ("expense", "other", True, False)


def test_rasa_must_be_sure_and_agree_for_auto_save():
    assert parse("đi ăn 200k", nlu("add_expense", 0.95)).entry.auto_save_ok is True
    assert parse("đi ăn 200k", nlu("add_expense", 0.80)).entry.auto_save_ok is False
    assert parse("đi ăn 200k", nlu("transfer_money", 0.99)).entry.auto_save_ok is False


def test_rasa_overconfident_income_is_ignored_when_the_sentence_has_a_clear_expense_keyword():
    parsed = parse("cắt kính cận 800k", nlu("add_income", 1.0))
    assert parsed.intent == "add_expense" and parsed.entry.category == "healthcare"
    assert parse("mua kính râm 300k").entry.category == "shopping"


def test_rasa_disagreement_trusts_the_sentence_unless_rasa_is_very_sure():
    assert parse("nhận lương 14 củ", nlu("add_expense", 0.90)).intent == "add_income"
    assert parse("250k", nlu("add_income", 0.99)).intent == "add_income"  # không có từ khóa chi tiêu nào, Rasa rất chắc thì thắng
    assert parse("250k", nlu("add_income", 0.90)).intent == "add_expense"


def test_low_rasa_confidence_falls_back_to_rules():
    parsed = parse("đi ăn 200k", nlu("goodbye", 0.40))
    assert (parsed.intent, parsed.nlu_source) == ("add_expense", "rules")


def test_rasa_can_cut_an_amount_the_rules_cannot_read():
    parsed = parse("gửi mẹ mười lăm", nlu("add_expense", 0.95, amount="2 triệu"))
    assert parsed.intent in ("add_expense", "needs_amount")


def test_missing_amount_asks_for_one():
    parsed = parse("ăn phở nhé", nlu("add_expense", 0.95))
    assert parsed.intent == "needs_amount" and parsed.entry is None and parsed.reply_text


@pytest.mark.parametrize(
    ("text", "metric"),
    [("tháng này tôi tiêu bao nhiêu", "expense"), ("tháng này thu nhập bao nhiêu", "income"), ("tháng này còn lại bao nhiêu", "balance")],
)
def test_totals_say_what_is_asked(text, metric):
    parsed = parse(text)
    assert parsed.intent == "ask_spending" and parsed.query.metric == metric
    assert parsed.query.period_key == "thang nay" and parsed.query.start == datetime(2026, 10, 1, tzinfo=VN)
    assert parsed.query.end == datetime(2026, 11, 1, tzinfo=VN)


def test_total_can_name_a_period_and_a_category():
    parsed = parse("hôm qua tôi chi cho ăn uống bao nhiêu")
    assert parsed.query.category == "dining" and parsed.query.period_key == "hom qua"
    assert parsed.query.end - parsed.query.start == (datetime(2026, 10, 14, tzinfo=VN) - datetime(2026, 10, 13, tzinfo=VN))


def test_period_ranges():
    assert period_range("tuan nay", NOW)[0] == datetime(2026, 10, 12, tzinfo=VN)  # thứ Hai
    assert period_range("tuan truoc", NOW) == (datetime(2026, 10, 5, tzinfo=VN), datetime(2026, 10, 12, tzinfo=VN))
    assert period_range("thang truoc", NOW) == (datetime(2026, 9, 1, tzinfo=VN), datetime(2026, 10, 1, tzinfo=VN))


@pytest.mark.parametrize("text", ["iphone 20 triệu có đáng mua không", "nên mua nhà hay thuê nhà", "lương 20 triệu có đủ sống không?"])
def test_questions_with_an_amount_are_not_entries(text):
    assert parse(text).intent == "out_of_scope"


def test_small_talk_and_empty_text_get_a_canned_reply():
    assert parse("xin chào").intent == "greet" and parse("xin chào").reply_text
    assert parse("cảm ơn").intent == "thanks"
    assert parse("   ").intent == "empty"
    assert parse("hôm nay trời đẹp quá").intent == "out_of_scope"


def test_unclear_reasons():
    assert parse("đi ăn 200k", nlu("add_expense", 0.99)).unclear_reason is None
    assert parse("đi ăn 200k", nlu("add_expense", 0.70)).unclear_reason == "low_confidence"
    assert parse("nhận lương 14 củ", nlu("add_expense", 0.90)).unclear_reason == "disagree"
    assert parse("hôm nay trời đẹp quá").unclear_reason == "not_understood"
    assert parse("iphone 20 triệu có đáng mua không").unclear_reason is None  # câu hỏi, không phải tin khó hiểu
