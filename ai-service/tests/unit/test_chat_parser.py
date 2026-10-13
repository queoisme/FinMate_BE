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
    assert (entry.kind, entry.amount, entry.category, entry.auto_save_ok) == ("expense", 200_000, "food", True)
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
    assert parsed.intent == "add_expense" and parsed.entry.category == "health"
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


def test_three_expenses_in_one_sentence_become_three_entries():
    parsed = parse("ăn cơm 20k, uống nước 10k, đổ xăng 50k")
    assert [e.amount for e in parsed.entries] == [20_000, 10_000, 50_000]
    assert [e.kind for e in parsed.entries] == ["expense"] * 3
    assert parsed.entries[0].category == "food" and parsed.entries[2].category == "transport"
    assert parsed.entry == parsed.entries[0]


def test_and_splits_two_entries():
    assert [e.amount for e in parse("ăn phở 45k và cà phê 30k").entries] == [45_000, 30_000]


def test_mixed_kinds_are_read_per_clause():
    parsed = parse("nhận lương 12 triệu, ăn cơm 20k")
    assert [(e.kind, e.amount) for e in parsed.entries] == [("income", 12_000_000), ("expense", 20_000)]


def test_decimal_comma_thousands_dot_and_quantity_stay_one_entry():
    for text, amount in (("mua đồ 1,5tr", 1_500_000), ("đi taxi 1.250.000", 1_250_000), ("mua 2 lít sữa 50k", 50_000)):
        parsed = parse(text)
        assert [e.amount for e in parsed.entries] == [amount], text


def test_multi_entries_are_never_auto_save_ok_even_with_rasa_absent():
    assert not any(e.auto_save_ok for e in parse("ăn cơm 20k, đổ xăng 50k").entries)


def test_single_sentence_still_has_one_entry_in_entries():
    parsed = parse("ăn cơm 20k")
    assert parsed.entries == (parsed.entry,) and parsed.entry.auto_save_ok


def test_non_entry_intents_have_no_entries():
    assert parse("tháng này tôi tiêu bao nhiêu?").entries == ()


def test_entry_carries_the_date_and_loses_the_date_words():
    entry = parse("ăn phở 45k hôm qua").entry
    assert entry.occurred_at == datetime(2026, 10, 13, 12, 0, tzinfo=VN) and entry.description == "ăn phở"


def test_sentence_date_applies_to_every_clause_without_its_own():
    parsed = parse("hôm qua ăn phở 45k, cà phê 30k")
    assert [e.occurred_at for e in parsed.entries] == [datetime(2026, 10, 13, 12, 0, tzinfo=VN)] * 2


def test_no_date_means_none():
    assert parse("ăn phở 45k").entry.occurred_at is None


def test_leftover_amount_in_the_description_is_never_auto_saved():
    entry = parse("ăn 20k,30k").entry  # hai số tiền mà không tách được: không ghi ngay một con số có thể sai
    assert entry.auto_save_ok is False


def test_a_normal_single_amount_is_still_auto_saved():
    assert parse("ăn phở 45k").entry.auto_save_ok is True


def test_chat_category_equals_the_shared_resolver_for_known_phrases():
    from app.core.category_resolver import resolve

    for phrase in ["ta quan Bobby", "sua bot Enfa", "hoc phi", "kham benh", "bot giat Omo", "vitamin C", "nuoc ngot Pepsi", "khan uot", "kem danh rang", "cat toc"]:
        assert parse(phrase + " 50k").entry.category == resolve(phrase).slug, phrase


def test_receipt_style_goods_now_get_a_category_in_chat():
    assert parse("bột giặt 80k").entry.category == "shopping"
    assert parse("vitamin C 120k").entry.category == "health"
    assert parse("khăn ướt 20k").entry.category == "shopping"  # trước đây chat xếp khăn ướt vào ăn uống


def test_category_words_typed_by_the_user_still_count():
    assert parse("chi cho giáo dục 500k").entry.category == "education"


def test_unknown_stays_unknown_and_is_not_auto_saved():
    entry = parse("mua nước 20k").entry
    assert entry.category is None and entry.auto_save_ok is False


def test_chat_never_fuzzy_matches_ordinary_sentences():
    assert parse("hôm nay trời đẹp quá").intent == "out_of_scope"  # "đẹp quá" ~ "dép quai" nếu dùng khớp gần đúng của OCR


# ---- phủ định và hủy: "mua cà phê 30k rồi hủy" không được ghi 30.000
def test_cancelled_only_item_records_nothing():
    parsed = parse("mua cà phê 30k rồi hủy")
    assert parsed.entries == ()
    assert parsed.intent == "out_of_scope"
    assert parsed.unclear_reason == "cancelled"


def test_spoken_correction_replaces_the_amount():
    parsed = parse("mua cà phê 30k, nhầm, là 35k")
    assert [e.amount for e in parsed.entries] == [35_000]


def test_cancel_only_drops_the_item_before_it():
    parsed = parse("ăn phở 40k rồi hủy, mua cà phê 30k")
    assert [e.amount for e in parsed.entries] == [30_000]


@pytest.mark.parametrize("text", ["không cần hủy, ghi cà phê 30k", "mua hủ tiếu 30k"])
def test_words_that_only_look_like_cancellation_are_kept(text):
    assert [e.amount for e in parse(text).entries] == [30_000]


# ---- "tổng cộng 60k" là số để đối chiếu, không phải một khoản chi
def test_stated_total_is_a_check_not_an_expense():
    parsed = parse("mua bánh mì 25k và cà phê 35k tổng cộng 60k")
    assert [e.amount for e in parsed.entries] == [25_000, 35_000]
    assert (parsed.total_amount, parsed.total_mismatch) == (60_000, False)


def test_stated_total_that_differs_is_flagged():
    parsed = parse("mua bánh mì 25k và cà phê 35k tổng cộng 70k")
    assert [e.amount for e in parsed.entries] == [25_000, 35_000]
    assert (parsed.total_amount, parsed.total_mismatch) == (70_000, True)


def test_word_total_inside_an_item_name_is_not_a_total():
    parsed = parse("mua bộ tổng hợp đề 150k")
    assert [e.amount for e in parsed.entries] == [150_000]
    assert parsed.total_amount is None


# ---- "hai trăm rưỡi" và vế thiếu tiền
def test_half_hundred_reads_like_the_spoken_equivalent():
    assert parse_amount("ăn cơm hai trăm rưỡi").value == parse_amount("ăn cơm hai trăm năm mươi").value == 250
    assert parse_amount("mua áo một triệu rưỡi").value == 1_500_000  # không đổi


def test_clause_without_amount_is_reported_not_merged():
    parsed = parse("ăn phở 40k và cà phê")
    assert [(e.amount, e.description) for e in parsed.entries] == [(40_000, "ăn phở")]
    assert parsed.skipped == ("cà phê",)


@pytest.mark.parametrize("text", ["ăn cơm 20k, 2 người", "ăn cơm 20k và 2 người", "ăn phở 40k và uống trà sữa 30k"])
def test_side_notes_and_full_items_are_not_reported_as_skipped(text):
    assert parse(text).skipped == ()


# ---- nguồn của danh mục, để app biết khi nào bộ nhớ riêng được phép thắng
def test_category_source_is_rule_when_keywords_decide():
    assert parse("ăn phở 45k").entry.category_source == "rule"


def test_category_source_is_none_when_nothing_decides():
    entry = parse("zxqv gizmo 50k").entry
    assert (entry.category, entry.category_source) == (None, None)


def test_a_skipped_clause_blocks_auto_save():
    # "ăn phở 40k và cà phê": người dùng nói hai thứ nhưng chỉ có một số tiền; không được ghi ngay một khoản có thể thiếu
    assert parse("ăn phở 40k và cà phê").entry.auto_save_ok is False


# ---- cách nói đời thường mà giọng nói hay trả ra: "hai lăm ngàn" = 25.000, "ngàn" = "nghìn", "ba mốt nghìn" = 31.000
@pytest.mark.parametrize(
    ("text", "amount"),
    [
        ("mua cơm hai lăm ngàn", 25_000),
        ("mua cơm hai lăm nghìn", 25_000),
        ("mua cơm bốn lăm ngàn", 45_000),
        ("mua cơm ba mốt nghìn", 31_000),
        ("mua cơm bốn mươi ngàn", 40_000),
        ("mua cơm ba ngàn", 3_000),
        ("mua cơm hai mươi lăm nghìn", 25_000),  # cách nói đầy đủ không đổi
    ],
)
def test_colloquial_spoken_amounts(text, amount):
    assert parse_amount(text).value == amount


@pytest.mark.parametrize("text", ["rút tiền ở ngân hàng", "hôm nay ba bốn người ăn cơm"])
def test_words_that_only_look_like_amounts_are_not_money(text):
    assert parse_amount(text) is None


def test_digits_still_win_over_number_words_in_the_same_sentence():
    assert parse_amount("hai ba người ăn cơm 50k").value == 50_000


@pytest.mark.parametrize("text", ["rồi mua cơm hai lăm ngàn", "và mua cơm hai lăm ngàn", "còn mua cơm 25k"])
def test_leading_connector_is_not_part_of_the_description(text):
    assert parse(text).entry.description == "mua cơm"


# ---- đợt sửa sau review: mọi tín hiệu "có thể sai" đều chặn tự ghi
def test_a_mismatched_total_blocks_auto_save():
    for text in ["ăn trưa 50k, mời sếp tổng 200k", "ăn phở 40k tổng cộng 45k"]:
        parsed = parse(text)
        assert parsed.entry.auto_save_ok is False, text


def test_boss_title_is_not_a_total_label():
    parsed = parse("ăn trưa 50k, mời sếp tổng 200k")
    assert parsed.total_amount is None
    assert [e.amount for e in parsed.entries] == [50_000, 200_000]


@pytest.mark.parametrize(
    "text",
    [
        "mua cà phê 30k. Hủy",
        "mua cà phê 30k, à thôi hủy đi",
        "mua cà phê 30k, thôi khỏi",
        "mua cà phê 30k, rồi hủy",
        "mua cà phê 30k nhưng hủy rồi",
        "ăn cơm 20k, 2 người, hủy",
    ],
)
def test_more_ways_of_cancelling_never_auto_save(text):
    parsed = parse(text)
    assert not any(e.auto_save_ok for e in parsed.entries), text


def test_cancel_after_a_side_note_drops_the_item_not_the_note():
    assert parse("ăn cơm 20k, 2 người, hủy").entries == ()


def test_amountless_clause_after_a_comma_is_reported_too():
    parsed = parse("ăn phở 40k, cà phê")
    assert [(e.amount, e.description) for e in parsed.entries] == [(40_000, "ăn phở")]
    assert parsed.skipped == ("cà phê",)
    assert parsed.entry.auto_save_ok is False


@pytest.mark.parametrize("text", ["chuyển khoản 500k cho mẹ và em", "ăn trưa 50k với Lan và Hoa", "ăn phở 40k rồi về nhà"])
def test_people_and_places_are_not_reported_as_missing_items(text):
    assert parse(text).skipped == ()


@pytest.mark.parametrize(("text", "amount"), [("mua nhà ba triệu rưỡi", 3_500_000), ("mua cơm hai trăm nghìn rưỡi", 200_500), ("mua áo hai triệu rưỡi", 2_500_000), ("mua áo triệu rưỡi", 1_500_000)])
def test_half_after_a_spoken_number_keeps_the_number(text, amount):
    assert parse_amount(text).value == amount


# ---- "cho" (đưa) không phải "chợ": bỏ dấu làm hai từ trùng nhau và mọi câu có chữ "cho" bị xếp vào Ăn uống
@pytest.mark.parametrize("text", ["mẹ tôi cho ba triệu", "mẹ cho tôi 3 triệu", "bố tôi cho 500k"])
def test_money_given_to_the_user_is_income_even_without_rasa(text):
    parsed = parse(text)
    assert parsed.intent == "add_income"
    assert (parsed.entry.kind, parsed.entry.income_type) == ("income", "gift")


def test_money_given_to_the_user_is_income_when_rasa_agrees():
    assert parse("mẹ tôi cho ba triệu", nlu("add_income", 1.0)).entry.kind == "income"


@pytest.mark.parametrize("text", ["cho mẹ 500k", "cho bạn vay 200k", "tôi cho em 100k"])
def test_giving_money_away_is_not_food(text):
    entry = parse(text).entry
    assert entry.category is None
    assert entry.auto_save_ok is False


@pytest.mark.parametrize("text", ["đi chợ 150k", "chợ 150k", "ra chợ mua rau 80k", "di cho 150k"])
def test_the_market_is_still_food(text):
    assert parse(text).entry.category == "food"
