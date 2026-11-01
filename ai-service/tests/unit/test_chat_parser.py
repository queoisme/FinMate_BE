"""Hiểu câu chat: loại giao dịch, số tiền lóng, danh mục, câu hỏi tổng, và khi nào đủ chắc để ghi ngay."""

from datetime import datetime, timedelta

import pytest

from app.services.amount_normalizer import parse_amount
from app.services.transaction_parser import VN, parse_chat, period_range

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
        # từ lóng, tỷ và chục: cùng bộ đọc và cùng amount_slang.json với voice
        ("hai củ tiền trọ", 2_000_000),
        ("mua điện thoại năm chai", 5_000_000),
        ("tiền nhà 3 chai", 3_000_000),
        ("đi ăn ba lít rưỡi", 350_000),
        ("mua giày hai lốp", 200_000),
        ("mua nhà ba tỏi hai", 3_200_000_000),
        ("chốt căn hộ 5 tỏi", 5_000_000_000),
        ("căn hộ 3 tỷ", 3_000_000_000),
        ("bán nhà 2 tỷ 5", 2_500_000_000),
        ("bán xe được một tỷ hai", 1_200_000_000),
        ("ăn phở hai chục ngàn đồng", 20_000),
        ("đổ xăng năm chục nghìn", 50_000),
        ("ăn tối hai mươi nhăm nghìn", 25_000),
        ("bốn lăm ngàn cà phê", 45_000),
        ("mua laptop hai mươi ba triệu năm trăm", 23_500_000),
        ("một triệu hai tiền điện thoại", 1_200_000),
        ("tiền thuê nhà tám triệu rưỡi một tháng", 8_500_000),
    ],
)
def test_slang_and_written_amounts(text, amount):
    assert parse_amount(text).value == amount


@pytest.mark.parametrize(
    "text",
    [
        "mua hai củ cải",
        "đổ hai lít xăng",
        "hai chai bia",
        "mua hai chai coca",
        "ăn hai tối",  # bỏ dấu thì "tối" = "tỏi": từ lóng chỉ nhận khi viết đúng dấu
        "ngủ 5 tối",
        "mua hai lốp xe",
    ],
)
def test_ordinary_word_that_sounds_like_slang_is_not_money(text):
    assert parse_amount(text) is None


@pytest.mark.parametrize(("text", "phrase"), [("mua hai củ cải", "hai củ"), ("đổ hai lít xăng", "hai lít"), ("mua hai chai bia", "hai chai")])
def test_rasa_amount_entity_cannot_turn_an_ordinary_word_into_money(text, phrase):
    """Rasa cắt "hai củ" ra khỏi "mua hai củ cải"; đọc riêng cụm đó thì mất chữ "cải" và ra 2 triệu."""
    parsed = parse(text, nlu("add_expense", amount=phrase))
    assert parsed.entries == () and parsed.intent == "needs_amount"


def test_rasa_ask_spending_does_not_turn_a_spend_with_an_amount_into_a_summary():
    """"cho con đi học một triệu" từng ra bảng "tóm tắt chi tiêu tháng này" của danh mục Gia đình."""
    parsed = parse("cho con đi học một triệu", nlu("ask_spending"))
    assert (parsed.intent, parsed.entry.kind, parsed.entry.amount, parsed.entry.category) == ("add_expense", "expense", 1_000_000, "family")


@pytest.mark.parametrize("text", ["tháng này tôi tiêu bao nhiêu", "tháng này tôi tiêu bao nhiêu cho ăn uống"])
def test_a_real_question_stays_a_question(text):
    assert parse(text, nlu("ask_spending")).intent == "ask_spending"


def test_the_same_unit_word_twice_is_judged_one_occurrence_at_a_time():
    """"ba lít rượu" là số lượng, "hết một lít" là giá: một chữ "rượu" không được loại cả hai."""
    parsed = parse("đi chợ mua ba lít rượu hết một lít")
    assert (parsed.entry.amount, parsed.entry.category) == (100_000, "food")
    assert parse("đi chợ mua ba lít rượu hết hai lít rưỡi").entry.amount == 250_000


@pytest.mark.parametrize("text", ["sữa tươi hai lít", "đổ hai lít xăng", "mua ba lít sữa hai lít rượu"])
def test_litres_of_liquid_are_still_not_money_when_there_is_no_price(text):
    assert parse(text).entries == ()


def test_asking_about_two_categories_returns_both():
    query = parse("tháng này tôi đã tiêu bao nhiêu tiền cho giải trí và mua sắm").query
    assert query.categories == ("entertainment", "shopping")


def test_asking_about_one_category_is_unchanged():
    query = parse("tháng này tôi tiêu bao nhiêu cho ăn uống").query
    assert query.categories == () and query.category is not None


@pytest.mark.parametrize(
    "text",
    ["đặt mục tiêu vào tháng tiếp theo tôi sẽ tiết kiệm được hai mươi triệu", "đặt mục tiêu 2 tháng sau mua xe máy khoảng 50 triệu", "tạo mục tiêu tiết kiệm 10 triệu"],
)
def test_setting_a_goal_is_not_recorded_as_a_transaction_but_becomes_a_goal_draft(text):
    parsed = parse(text, nlu("transfer_money", amount="hai mươi triệu"))
    assert parsed.entries == () and parsed.intent == "create_goal"
    assert parsed.goal is not None and parsed.goal.target > 0


@pytest.mark.parametrize("text", ["chuyển hai triệu sang tiết kiệm", "gửi tiết kiệm năm triệu"])
def test_moving_money_to_savings_is_still_a_transfer(text):
    assert parse(text).entry.kind == "transfer"


def test_chai_is_a_bottle_count_when_the_sentence_has_a_price():
    assert parse_amount("uống 50k hai chai").value == 50_000


def test_expense_with_a_known_category_is_auto_saveable():
    entry = parse("đi ăn 200k").entry
    assert (entry.kind, entry.amount, entry.category, entry.auto_save_ok) == ("expense", 200_000, "food", True)
    assert entry.description == "đi ăn"


@pytest.mark.parametrize("text", ["đi chơi với bạn 200k", "đi chơi hết 500k", "chơi game 50k"])
def test_going_out_is_entertainment(text):
    assert parse(text).entry.category == "entertainment"


def test_one_sentence_with_spend_income_spend_gives_one_entry_each():
    """Mỗi khoản một dòng, kể cả hai khoản chi cùng danh mục (Giải trí): thẻ xác nhận cần thấy từng khoản để tính tổng ròng."""
    entries = parse("xem phim một triệu mẹ cho mười triệu đi chơi với trai hai triệu").entries
    assert [(e.kind, e.amount, e.category) for e in entries] == [
        ("expense", 1_000_000, "entertainment"),
        ("income", 10_000_000, None),
        ("expense", 2_000_000, "entertainment"),
    ]


@pytest.mark.parametrize(
    ("text", "category"),
    [
        ("đi chợ mua nồi 100k", "shopping"),
        ("mua nồi cơm điện 800k", "shopping"),
        ("mua đồ gia dụng 500k", "shopping"),
        ("đi siêu thị mua đồ gia dụng 500k", "shopping"),
        ("đi chợ 200k", "food"),  # "đi chợ" một mình vẫn là Ăn uống
        ("đi chợ mua áo 200k", "shopping"),
    ],
)
def test_cookware_and_household_goods_are_shopping(text, category):
    assert parse(text).entry.category == category


@pytest.mark.parametrize(
    "text",
    ["tôi cho mẹ hai củ", "cho mẹ 500k", "cho me 500k", "cho ba mẹ 500k", "cho bố 300k", "cho chị 200k", "cho anh 1 triệu", "cho em 200k", "cho vợ 1 triệu"],
)
def test_giving_money_to_family_is_the_family_category(text):
    assert parse(text).entry.category == "family"


@pytest.mark.parametrize("text", ["cho chi phí 500k", "cho chủ nhà 3 triệu", "cho bác sĩ 200k", "đặt bàn cho ba người 500k", "cho bạn 100k"])
def test_cho_followed_by_a_word_that_is_not_a_relative_is_not_family(text):
    assert parse(text).entry.category != "family"


def test_money_received_from_a_parent_is_still_income():
    assert parse("mẹ cho tôi hai củ").entry.kind == "income"
    assert parse("mẹ cho em 500k").entry.kind == "income"


def test_rasa_out_of_scope_does_not_reject_a_sentence_with_amount_and_a_clear_expense_word():
    """Cùng một câu chỉ đổi "có" thành "còn" từng bị Rasa chấm "ngoài chủ đề" một lần và hiểu một lần: có số tiền và từ khóa chi tiêu thì luật giữ quyền."""
    out_of_scope = nlu("out_of_scope", 0.95)
    parsed = parse("tôi có đi mua một bộ quần áo hết hai triệu tôi cho mẹ hai củ mẹ cho tôi hai tỏi", out_of_scope)
    assert [(e.kind, e.amount) for e in parsed.entries] == [("expense", 2_000_000), ("expense", 2_000_000), ("income", 2_000_000_000)]
    assert parse("tôi có đi mua một bộ quần áo hết hai triệu", out_of_scope).intent == "add_expense"


@pytest.mark.parametrize("text", ["tôi có 2 triệu trong ví", "hôm nay trời đẹp 2 triệu"])
def test_rasa_out_of_scope_still_wins_when_there_is_no_expense_cue(text):
    assert parse(text, nlu("out_of_scope", 0.95)).intent == "out_of_scope"


@pytest.mark.parametrize(
    ("text", "amount"),
    [("sau đó trả năm chục nghìn", 50_000), ("trả góp xe máy một triệu tám", 1_800_000), ("thanh toán hai triệu", 2_000_000)],
)
def test_rasa_out_of_scope_does_not_reject_paying_an_amount(text, amount):
    """Trả/thanh toán đi với số tiền là khoản chi dù chưa có từ khoá danh mục; Rasa chấm "ngoài chủ đề" thì luật giữ quyền."""
    parsed = parse(text, nlu("out_of_scope", 0.95))
    assert parsed.intent == "add_expense"
    assert parsed.entry.amount == amount


@pytest.mark.parametrize("text", ["trả lời 5 câu hỏi", "trả lại 2 triệu trong ví"])
def test_rasa_out_of_scope_still_wins_for_tra_that_is_not_paying(text):
    assert parse(text, nlu("out_of_scope", 0.95)).intent == "out_of_scope"


@pytest.mark.parametrize(
    ("text", "category"),
    [("mua điện thoại năm chai", "shopping"), ("mua điện thoại 5 triệu", "shopping"), ("mua laptop 20 triệu", "shopping"), ("nạp tiền điện thoại 100k", "bills"), ("tiền điện thoại 200k", "bills")],
)
def test_buying_a_phone_or_laptop_is_shopping_but_phone_credit_is_bills(text, category):
    assert parse(text).entry.category == category


@pytest.mark.parametrize(
    ("text", "amount"),
    [
        ("ăn phở 45 000 đồng", 45_000),
        ("mua sách 120 000đ", 120_000),
        ("ăn phở 2 chục ngàn đồng", 20_000),
        ("ăn phở 3 chục ngàn", 30_000),
        ("mua 3 trăm nghìn", 300_000),
        ("ăn phở 2 mươi lăm nghìn", 25_000),
        ("mua sách 1tr350", 1_350_000),
        ("mua sách 1tr35", 1_350_000),
        ("mua 2tr500", 2_500_000),
    ],
)
def test_spaced_thousands_digit_tens_and_trailing_three_digits(text, amount):
    assert parse_amount(text).value == amount


@pytest.mark.parametrize("text", ["ăn lúc 3 120", "ăn 2 phần 100", "mua 2 mươi cái bánh"])
def test_numbers_that_only_look_like_the_new_formats_are_not_money(text):
    assert parse_amount(text) is None


@pytest.mark.parametrize("text", ["nói chuyện với mẹ 50k", "nơi này đắt 200k", "nối mạng 100k"])
def test_noi_without_the_accent_of_noi_pot_is_not_shopping(text):
    """"nồi" khớp từ khoá chỉ khi viết đúng dấu: bỏ dấu thì trùng nói/nơi/nối."""
    assert parse(text).entry.category != "shopping"


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


def test_year_and_numbered_month_ranges():
    assert period_range("nam nay", NOW) == (datetime(2026, 1, 1, tzinfo=VN), datetime(2027, 1, 1, tzinfo=VN))
    assert period_range("nam ngoai", NOW) == period_range("nam truoc", NOW) == (datetime(2025, 1, 1, tzinfo=VN), datetime(2026, 1, 1, tzinfo=VN))
    assert period_range("thang 9/2026", NOW) == (datetime(2026, 9, 1, tzinfo=VN), datetime(2026, 10, 1, tzinfo=VN))
    assert period_range("thang 12/2025", NOW) == (datetime(2025, 12, 1, tzinfo=VN), datetime(2026, 1, 1, tzinfo=VN))  # tháng 12 sang năm sau
    assert period_range("thang 2/2024", NOW) == (datetime(2024, 2, 1, tzinfo=VN), datetime(2024, 3, 1, tzinfo=VN))


@pytest.mark.parametrize(
    ("text", "key", "label", "start", "end"),
    [
        ("tháng 9 tôi tiêu bao nhiêu", "thang 9/2026", "tháng 9", datetime(2026, 9, 1, tzinfo=VN), datetime(2026, 10, 1, tzinfo=VN)),
        ("thang 09 toi tieu bao nhieu", "thang 9/2026", "tháng 9", datetime(2026, 9, 1, tzinfo=VN), datetime(2026, 10, 1, tzinfo=VN)),
        ("tháng 10 tôi tiêu bao nhiêu", "thang 10/2026", "tháng 10", datetime(2026, 10, 1, tzinfo=VN), datetime(2026, 11, 1, tzinfo=VN)),  # đang là tháng 10
        ("tháng 12 tôi tiêu bao nhiêu", "thang 12/2025", "tháng 12/2025", datetime(2025, 12, 1, tzinfo=VN), datetime(2026, 1, 1, tzinfo=VN)),  # chưa tới: là năm ngoái
        ("tháng 9 năm 2025 tôi chi bao nhiêu", "thang 9/2025", "tháng 9/2025", datetime(2025, 9, 1, tzinfo=VN), datetime(2025, 10, 1, tzinfo=VN)),
        ("chi tiêu tháng 9/2025 là bao nhiêu", "thang 9/2025", "tháng 9/2025", datetime(2025, 9, 1, tzinfo=VN), datetime(2025, 10, 1, tzinfo=VN)),
        ("năm nay tôi tiêu bao nhiêu", "nam nay", "năm nay", datetime(2026, 1, 1, tzinfo=VN), datetime(2027, 1, 1, tzinfo=VN)),
        ("năm ngoái tôi tiêu bao nhiêu", "nam ngoai", "năm ngoái", datetime(2025, 1, 1, tzinfo=VN), datetime(2026, 1, 1, tzinfo=VN)),
        ("năm trước tôi chi bao nhiêu", "nam truoc", "năm trước", datetime(2025, 1, 1, tzinfo=VN), datetime(2026, 1, 1, tzinfo=VN)),
    ],
)
def test_a_question_about_a_named_month_or_year_gets_that_period_not_this_month(text, key, label, start, end):
    query = parse(text).query
    assert (query.period_key, query.period_label, query.start, query.end) == (key, label, start, end)


def _span(first, last):
    """(đầu, cuối không gồm) của khoảng ngày tính cả hai đầu."""
    return datetime(*first, tzinfo=VN), datetime(*last, tzinfo=VN) + timedelta(days=1)


@pytest.mark.parametrize(
    ("text", "label", "first", "last"),
    [
        ("ngày 5 tháng 10 tôi tiêu bao nhiêu", "ngày 5/10", (2026, 10, 5), (2026, 10, 5)),
        ("ngày 15 tháng 3 tôi tiêu bao nhiêu", "ngày 15/3", (2026, 3, 15), (2026, 3, 15)),
        ("ngày 15/3 tôi chi bao nhiêu", "ngày 15/3", (2026, 3, 15), (2026, 3, 15)),
        ("15/12 tôi chi bao nhiêu", "ngày 15/12/2025", (2025, 12, 15), (2025, 12, 15)),  # chưa tới năm nay: là năm ngoái
        ("ngày 5/10/2025 tôi tiêu bao nhiêu", "ngày 5/10/2025", (2025, 10, 5), (2025, 10, 5)),
        ("ngày 15 tháng 3 năm 2025 tôi chi bao nhiêu", "ngày 15/3/2025", (2025, 3, 15), (2025, 3, 15)),
        ("ngày 10 tôi chi bao nhiêu", "ngày 10/10", (2026, 10, 10), (2026, 10, 10)),
        ("ngày 20 tôi chi bao nhiêu", "ngày 20/9", (2026, 9, 20), (2026, 9, 20)),  # ngày 20 tháng này chưa tới: tháng trước
        ("hôm kia tôi chi bao nhiêu", "hôm kia", (2026, 10, 12), (2026, 10, 12)),
        ("3 ngày qua tôi tiêu bao nhiêu", "3 ngày qua", (2026, 10, 12), (2026, 10, 14)),  # gồm cả hôm nay
        ("7 ngày gần đây tôi chi bao nhiêu", "7 ngày qua", (2026, 10, 8), (2026, 10, 14)),
        ("2 tuần qua tôi chi bao nhiêu", "2 tuần qua", (2026, 10, 1), (2026, 10, 14)),
        ("3 tháng qua tôi tiêu bao nhiêu", "3 tháng qua", (2026, 7, 15), (2026, 10, 14)),
        ("quý 1 tôi tiêu bao nhiêu", "quý 1", (2026, 1, 1), (2026, 3, 31)),
        ("quý 4 tôi tiêu bao nhiêu", "quý 4", (2026, 10, 1), (2026, 12, 31)),
        ("quý 4 năm 2025 tôi chi bao nhiêu", "quý 4/2025", (2025, 10, 1), (2025, 12, 31)),
        ("quý này tôi tiêu bao nhiêu", "quý 4", (2026, 10, 1), (2026, 12, 31)),
        ("quý trước tôi tiêu bao nhiêu", "quý 3", (2026, 7, 1), (2026, 9, 30)),
        ("từ ngày 1 đến ngày 10 tôi chi bao nhiêu", "từ 1/10 đến 10/10", (2026, 10, 1), (2026, 10, 10)),
        ("từ ngày 20 đến ngày 25 tôi chi bao nhiêu", "từ 20/9 đến 25/9", (2026, 9, 20), (2026, 9, 25)),  # ngày 20 tháng này chưa tới
        ("từ 25/9 đến 5/10 tôi chi bao nhiêu", "từ 25/9 đến 5/10", (2026, 9, 25), (2026, 10, 5)),
        ("từ 5/10 đến 15/10 tôi tiêu bao nhiêu", "từ 5/10 đến 15/10", (2026, 10, 5), (2026, 10, 15)),
    ],
)
def test_a_question_about_a_day_or_a_span_of_days_gets_exactly_those_days(text, label, first, last):
    query = parse(text).query
    assert query.period_label == label
    assert (query.start, query.end) == _span(first, last)


def test_a_span_key_is_a_range_that_includes_both_ends():
    assert period_range("tu 2025-03-01 den 2025-03-10", NOW) == (datetime(2025, 3, 1, tzinfo=VN), datetime(2025, 3, 11, tzinfo=VN))
    assert period_range("tu 2025-12-31 den 2025-12-31", NOW) == (datetime(2025, 12, 31, tzinfo=VN), datetime(2026, 1, 1, tzinfo=VN))


@pytest.mark.parametrize(
    "text",
    ["tháng 13 tôi tiêu bao nhiêu", "tháng 0 tôi tiêu bao nhiêu", "ngày 31/2 tôi chi bao nhiêu", "cuối tuần rồi tôi tiêu bao nhiêu", "đầu tháng tôi tiêu bao nhiêu"],
)
def test_a_time_phrase_that_cannot_be_read_is_asked_back_not_answered_as_this_month(text):
    parsed = parse(text)
    assert parsed.intent == "needs_period" and parsed.query is None and parsed.reply_text and "hôm nay" in parsed.reply_text


@pytest.mark.parametrize("text", ["tôi tiêu bao nhiêu", "tiêu bao nhiêu cho quần áo nam", "tháng này tôi tiêu bao nhiêu"])
def test_a_question_without_any_time_word_still_defaults_to_this_month(text):
    assert parse(text).query.period_key == "thang nay"


@pytest.mark.parametrize(
    ("text", "key"),
    [("sáng nay tôi tiêu bao nhiêu", "hom nay"), ("chiều nay tôi chi bao nhiêu", "hom nay"), ("tối nay tôi chi bao nhiêu", "hom nay"), ("đêm qua tôi tiêu bao nhiêu", "hom qua")],
)
def test_a_part_of_the_day_is_that_whole_day(text, key):
    assert parse(text).query.period_key == key


def test_an_amount_range_is_not_a_range_of_days():
    parsed = parse("từ 3 đến 5 triệu tôi đã chi bao nhiêu")
    assert parsed.query is None or "đến" not in parsed.query.period_label


@pytest.mark.parametrize("text", ["7 ngày qua tôi tiêu bao nhiêu", "từ ngày 1 đến ngày 10 tôi chi bao nhiêu", "ngày 15/3 tôi chi bao nhiêu", "quý 1 tôi tiêu bao nhiêu"])
def test_a_day_span_question_beats_rasa_saying_out_of_scope(text):
    assert parse(text, {"intent": "out_of_scope", "confidence": 0.99, "entities": {}}).intent == "ask_spending"


def test_the_detail_flag_works_for_a_day_or_a_span():
    assert parse("chi tiết ngày 15/3").query.detail is True
    assert parse("liệt kê các khoản chi 7 ngày qua").query.detail is True


def test_a_bare_year_is_a_year_period():
    query = parse("năm 2025 tôi tiêu bao nhiêu").query
    assert (query.period_key, query.period_label, query.start, query.end) == (
        "nam 2025", "năm 2025", datetime(2025, 1, 1, tzinfo=VN), datetime(2026, 1, 1, tzinfo=VN))
    assert period_range("nam 2024", NOW) == (datetime(2024, 1, 1, tzinfo=VN), datetime(2025, 1, 1, tzinfo=VN))


@pytest.mark.parametrize(
    "text",
    ["năm 2025 tôi tiêu bao nhiêu", "tháng 9 năm 2025 tôi chi bao nhiêu", "tháng 9 tôi chi bao nhiêu", "năm ngoái tôi tiêu bao nhiêu", "tuần trước tôi tiêu bao nhiêu"],
)
def test_a_total_question_naming_a_period_beats_rasa_saying_out_of_scope(text):
    # Rasa từng đoán các câu có số năm là out_of_scope với độ chắc 0,86-0,98 vì dữ liệu huấn luyện chưa có cách nói kỳ này
    assert parse(text, nlu("out_of_scope", 0.98)).intent == "ask_spending"
    assert parse(text, nlu("out_of_scope", 0.98)).query is not None


def test_rasa_out_of_scope_still_wins_when_the_rules_see_no_period_or_no_total_question():
    assert parse("bạn có bao nhiêu tiền", nlu("out_of_scope", 0.98)).intent == "out_of_scope"  # câu xã giao hỏi bot
    assert parse("tháng 9 trời đẹp quá", nlu("out_of_scope", 0.98)).intent == "out_of_scope"  # có kỳ nhưng không phải câu hỏi tổng
    assert parse("iphone 20 triệu có đáng mua không", nlu("out_of_scope", 0.98)).intent == "out_of_scope"
    # kỳ cụ thể nhưng câu hỏi về chủ thể khác (không có "tôi", không có từ hỏi chi tiêu): kiến thức chung, vẫn theo Rasa
    assert parse("thưởng tết năm nay các công ty bao nhiêu", nlu("out_of_scope", 0.98)).intent == "out_of_scope"


def test_a_spend_question_without_a_pronoun_but_with_a_spend_word_still_beats_rasa():
    assert parse("năm 2025 tiêu bao nhiêu", nlu("out_of_scope", 0.98)).intent == "ask_spending"
    assert parse("tháng 9 mình thu được bao nhiêu", nlu("out_of_scope", 0.98)).intent == "ask_spending"


def test_a_low_confidence_out_of_scope_yields_to_the_rules_for_a_total_question():
    assert parse("tôi tiêu bao nhiêu", nlu("out_of_scope", 0.5)).intent == "ask_spending"


@pytest.mark.parametrize(
    ("text", "metric", "period_key"),
    [
        ("chi tiết chi tiêu hôm nay", "expense", "hom nay"),
        ("cho tôi xem chi tiết hôm nay tôi tiêu những gì", "expense", "hom nay"),
        ("liệt kê các khoản chi tháng này", "expense", "thang nay"),
        ("hôm nay tôi mua gì", "expense", "hom nay"),
        ("hôm qua tôi đã chi những gì", "expense", "hom qua"),
        ("tháng này tôi tiêu vào những khoản nào", "expense", "thang nay"),
        ("danh sách chi tiêu tuần này", "expense", "tuan nay"),
        ("chi tiết tháng 9", "expense", "thang 9/2026"),
        ("liệt kê các khoản thu tháng này", "income", "thang nay"),
        ("chi tiết thu nhập năm nay", "income", "nam nay"),
    ],
)
def test_asking_for_the_details_of_a_period_sets_the_detail_flag(text, metric, period_key):
    parsed = parse(text)
    assert parsed.intent == "ask_spending" and parsed.query.detail is True
    assert (parsed.query.metric, parsed.query.period_key) == (metric, period_key)


@pytest.mark.parametrize("text", ["tháng này tôi tiêu bao nhiêu", "tổng chi tiêu hôm nay là bao nhiêu", "năm nay tôi thu được bao nhiêu"])
def test_a_plain_total_question_is_not_a_detail_request(text):
    assert parse(text).query.detail is False


@pytest.mark.parametrize(
    "text",
    ["hôm nay tôi nên mua gì", "tháng này mình muốn mua gì", "chi tiết", "danh sách việc cần làm", "chi tiết hóa đơn này"],
)
def test_advice_and_context_free_words_are_not_a_detail_request(text):
    parsed = parse(text)
    assert parsed.query is None or parsed.query.detail is False
    assert parsed.intent != "ask_spending" or parsed.query.detail is False


@pytest.mark.parametrize("text", ["liệt kê các khoản chi tháng này", "hôm nay tôi mua gì", "chi tiết chi tiêu hôm nay"])
def test_a_detail_request_beats_rasa_saying_out_of_scope(text):
    parsed = parse(text, nlu("out_of_scope", 0.98))
    assert parsed.intent == "ask_spending" and parsed.query.detail is True


def test_the_detail_flag_keeps_the_category_and_the_period():
    parsed = parse("chi tiết ăn uống tháng 9")
    assert (parsed.query.detail, parsed.query.category, parsed.query.period_key) == (True, "dining", "thang 9/2026")


def test_the_named_month_keeps_the_metric_and_the_category():
    parsed = parse("tháng 9 tôi chi cho ăn uống bao nhiêu")
    assert (parsed.query.metric, parsed.query.category, parsed.query.period_key) == ("expense", "dining", "thang 9/2026")
    assert parse("tháng 9 tôi thu được bao nhiêu").query.metric == "income"


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


def test_three_expenses_in_one_sentence_become_one_entry_each():
    parsed = parse("ăn cơm 20k, uống nước 10k, đổ xăng 50k")
    assert [e.amount for e in parsed.entries] == [20_000, 10_000, 50_000]  # hai khoản Ăn uống cũng không gộp
    assert [e.kind for e in parsed.entries] == ["expense"] * 3
    assert [e.category for e in parsed.entries] == ["food", "food", "transport"]
    assert parsed.entry == parsed.entries[0]


def test_and_splits_two_entries():
    assert [e.amount for e in parse("ăn phở 45k và đổ xăng 30k").entries] == [45_000, 30_000]


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
    parsed = parse("hôm qua ăn phở 45k, đổ xăng 30k")
    assert [e.occurred_at for e in parsed.entries] == [datetime(2026, 10, 13, 12, 0, tzinfo=VN)] * 2


def test_no_date_means_none():
    assert parse("ăn phở 45k").entry.occurred_at is None


def test_leftover_amount_in_the_description_is_never_auto_saved():
    entry = parse("ăn 20k,30k").entry  # hai số tiền mà không tách được: không ghi ngay một con số có thể sai
    assert entry.auto_save_ok is False


def test_a_normal_single_amount_is_still_auto_saved():
    assert parse("ăn phở 45k").entry.auto_save_ok is True


def test_chat_category_equals_the_shared_resolver_for_known_phrases():
    from app.services.category_resolver import resolve

    for phrase in ["ta quan Bobby", "sua bot Enfa", "hoc phi", "kham benh", "bot giat Omo", "vitamin C", "nuoc ngot Pepsi", "khan uot", "kem danh rang", "cat toc"]:
        assert parse(phrase + " 50k").entry.category == resolve(phrase).slug, phrase


def test_receipt_style_goods_now_get_a_category_in_chat():
    assert parse("bột giặt 80k").entry.category == "shopping"
    assert parse("vitamin C 120k").entry.category == "health"
    assert parse("khăn ướt 20k").entry.category == "family"  # luật 6: khăn ướt là đồ cho em bé (trước đây ăn uống, rồi mua sắm)


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
    parsed = parse("mua bánh mì 25k và đổ xăng 35k tổng cộng 60k")
    assert [e.amount for e in parsed.entries] == [25_000, 35_000]
    assert (parsed.total_amount, parsed.total_mismatch) == (60_000, False)


def test_stated_total_that_differs_is_flagged():
    parsed = parse("mua bánh mì 25k và đổ xăng 35k tổng cộng 70k")
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
    assert parse(text).entry.category != "food"  # "cho" bỏ dấu từng trùng "chợ": đi cho tiền không phải đi chợ


def test_giving_money_to_a_friend_without_lending_is_unknown_so_the_user_picks():
    entry = parse("cho bạn 100k").entry
    assert entry.category is None
    assert entry.auto_save_ok is False


@pytest.mark.parametrize("text", ["cho mượn 500k", "cho vay 2 triệu", "tôi cho bạn mượn 500k", "cho bạn vay 200k", "cho em mượn 1 triệu", "cho mượn 2 lít"])
def test_lending_money_out_is_a_loan_expense_that_is_never_saved_without_asking(text):
    entry = parse(text).entry
    assert (entry.kind, entry.category, entry.to_others) == ("expense", "loan", True)
    assert entry.auto_save_ok is False  # kể cả khoản nhỏ, vì sau này người ta trả lại nên chưa chắc là chi tiêu thật


@pytest.mark.parametrize(
    "text",
    ["bạn trả nợ 500k", "bạn trả nợ tôi 500k", "bạn chuyển khoản trả nợ 300k", "anh Nam trả lại 1 triệu", "trả nợ cho tôi 200k", "bạn trả tôi 500k", "ban tra no 500k"],
)
def test_a_debt_paid_back_to_me_is_money_in_under_the_loan_category(text):
    entry = parse(text).entry
    assert (entry.kind, entry.category, entry.income_type, entry.to_others) == ("income", "loan", None, False)


@pytest.mark.parametrize("text", ["bạn cho tôi mượn 300k", "mượn bạn 1 triệu", "vay bạn 2 triệu"])
def test_borrowing_is_money_in_under_the_loan_category_too(text):
    entry = parse(text).entry
    assert (entry.kind, entry.category) == ("income", "loan")


@pytest.mark.parametrize("text", ["trả nợ 500k", "trả nợ cho bạn 300k", "trả nợ bạn 300k", "tôi trả nợ 2 triệu"])
def test_paying_my_own_debt_is_still_money_out_under_the_loan_category(text):
    entry = parse(text).entry
    assert (entry.kind, entry.category, entry.to_others, entry.auto_save_ok) == ("expense", "loan", True, False)


def test_sending_money_to_a_person_is_still_an_other_expense_not_a_loan():
    entry = parse("chuyển khoản cho mẹ 500k").entry
    assert entry.category != "loan"


def test_rasa_cannot_turn_a_friend_repaying_me_back_into_an_expense():
    """Rasa học "trả nợ bạn" là chuyển tiền ra nên với "bạn trả nợ" nó cũng trả transfer_money rất chắc."""
    parsed = parse("bạn trả nợ 500k", nlu("transfer_money", 0.99))
    assert (parsed.intent, parsed.entry.kind) == ("add_income", "income")


def test_lending_and_being_repaid_in_one_sentence_are_two_entries():
    entries = parse("cho bạn mượn 500k, bạn trả nợ 300k").entries
    assert [(e.kind, e.amount) for e in entries] == [("expense", 500_000), ("income", 300_000)]


@pytest.mark.parametrize("text", ["đi chợ 150k", "chợ 150k", "ra chợ mua rau 80k", "di cho 150k"])
def test_the_market_is_still_food(text):
    assert parse(text).entry.category == "food"


@pytest.mark.parametrize("text", ["cho mẹ 500k", "tôi cho em 100k", "cho bạn vay 200k"])
def test_giving_money_away_stays_an_expense_even_when_rasa_says_income(text):
    # Rasa v5 trả add_income 1.0 cho "cho mẹ 500k" (lẫn với "mẹ cho"): tiền đi ra
    parsed = parse(text, nlu("add_income", 1.0))
    assert (parsed.intent, parsed.entry.kind) == ("add_expense", "expense")


# ---- không có dấu phẩy/"và" giữa các khoản (giọng nói không có dấu câu): vẫn là từng khoản riêng, không được mất khoản nào
def test_items_with_no_separator_between_them_are_split_by_their_amounts():
    parsed = parse("ăn cơm 25k đổ xăng 10k")
    assert [(e.amount, e.description) for e in parsed.entries] == [(25_000, "ăn cơm"), (10_000, "đổ xăng")]
    assert not any(e.auto_save_ok for e in parsed.entries)


def test_spoken_items_without_punctuation_are_split_too():
    parsed = parse("ăn cơm hai lăm ngàn đổ xăng mười ngàn")
    assert [e.amount for e in parsed.entries] == [25_000, 10_000]


def test_amount_first_style_is_split_before_the_next_amount():
    parsed = parse("25k ăn cơm 10k đổ xăng")
    assert [(e.amount, e.description) for e in parsed.entries] == [(25_000, "ăn cơm"), (10_000, "đổ xăng")]


@pytest.mark.parametrize("text", ["mua 2 lít sữa 50k", "mua sữa 50k hộp 2 lít", "ăn cơm 25k cho 2 người", "mua sữa rửa mặt 80k"])
def test_quantities_and_side_notes_do_not_cause_a_split(text):
    assert len(parse(text).entries) == 1


# ---- "trong 100k có 20k ăn cơm và 80k mua sữa rửa mặt": 100k là tổng để đối chiếu, hai khoản là 20k và 80k
@pytest.mark.parametrize("text", ["trong 100k có 20k ăn cơm và 80k mua sữa rửa mặt", "100k gồm ăn cơm 20k và mua sữa rửa mặt 80k"])
def test_a_breakdown_of_a_stated_total_becomes_the_items_plus_a_check(text):
    parsed = parse(text)
    assert [(e.amount, e.category) for e in parsed.entries] == [(20_000, "food"), (80_000, "shopping")]
    assert [e.description for e in parsed.entries] == ["ăn cơm", "mua sữa rửa mặt"]
    assert (parsed.total_amount, parsed.total_mismatch) == (100_000, False)


def test_a_breakdown_that_does_not_add_up_is_flagged():
    parsed = parse("trong 100k có 20k ăn cơm và 70k mua sữa rửa mặt")
    assert (parsed.total_amount, parsed.total_mismatch) == (100_000, True)
    assert not any(e.auto_save_ok for e in parsed.entries)


# ---- mỗi khoản một dòng, cùng danh mục hay không
def test_items_of_the_same_category_stay_separate_entries():
    parsed = parse("ăn cơm 25k uống nước 10k")
    assert [(e.amount, e.category, e.description) for e in parsed.entries] == [(25_000, "food", "ăn cơm"), (10_000, "food", "uống nước")]
    assert parsed.entry is parsed.entries[0]
    assert parsed.reply_text is None  # không còn lời nhắn "Mình gộp..."
    assert not any(e.auto_save_ok for e in parsed.entries)  # nhiều khoản không bao giờ ghi ngay


def test_spoken_items_of_the_same_category_stay_separate_too():
    assert [e.amount for e in parse("ăn cơm hai lăm ngàn uống nước mười ngàn").entries] == [25_000, 10_000]


def test_the_sentence_from_the_screenshot_keeps_snack_drink_and_market_apart():
    parsed = parse("bánh tráng trộn hai mươi ngàn trà sữa năm mươi ngàn đi chợ hai trăm ngàn bánh tráng")
    assert [(e.amount, e.category) for e in parsed.entries] == [(20_000, "food"), (50_000, "food"), (200_000, "food")]


def test_items_of_different_categories_stay_separate():
    parsed = parse("ăn cơm 25k và mua sữa rửa mặt 80k")
    assert [(e.amount, e.category) for e in parsed.entries] == [(25_000, "food"), (80_000, "shopping")]
    assert parsed.reply_text is None


def test_unknown_categories_stay_separate():
    parsed = parse("zxqv gizmo 50k, qwrt plugh 30k")
    assert [(e.amount, e.category) for e in parsed.entries] == [(50_000, None), (30_000, None)]


def test_items_on_different_days_stay_separate():
    parsed = parse("hôm qua ăn cơm 25k, hôm nay uống nước 10k")
    assert [e.amount for e in parsed.entries] == [25_000, 10_000]


def test_candy_bought_for_the_child_is_food_not_family():
    """"cho con 10k mua kẹo" và "mua kẹo hết 10k cho con" từng ra Gia đình vì cụm người nhận "cho con" thắng; kẹo là món ăn."""
    parsed = parse("mua bó rau mồng tơi 25k, mua một cái bút chì 10k, cho con 10k mua kẹo, mua kẹo hết 10k cho con")
    assert [(e.amount, e.category) for e in parsed.entries] == [(25_000, "food"), (10_000, "education"), (10_000, "food"), (10_000, "food")]


def test_a_stated_total_is_compared_with_the_sum_of_the_entries():
    parsed = parse("trong 30k có 20k ăn cơm và 10k uống nước")
    assert [e.amount for e in parsed.entries] == [20_000, 10_000]
    assert (parsed.total_amount, parsed.total_mismatch) == (30_000, False)


# ---- hợp đồng giữa mẫu kỳ trong config và code: admin sửa hỏng phải bị chặn, không được làm chat sập

def _doc_with(pattern, old, new):
    import copy

    from app.services import intent_cues

    doc = copy.deepcopy(intent_cues.default_data())
    assert old in doc["patterns"][pattern]["regex"]
    doc["patterns"][pattern]["regex"] = doc["patterns"][pattern]["regex"].replace(old, new)
    return doc


@pytest.mark.parametrize(
    ("pattern", "group"),
    [("period_day", "month"), ("period_range", "d2"), ("period_last", "weeks"), ("period_quarter", "previous"), ("period_day_part", "today"), ("period_month", "month"), ("period_year", "year")],
)
def test_a_period_pattern_missing_a_group_the_code_reads_is_rejected(pattern, group):
    from app.bill.rules_admin import RuleError
    from app.services import config_documents, intent_cues

    doc = _doc_with(pattern, f"(?P<{group}>", "(?P<renamed>")
    with pytest.raises(ValueError, match=group):
        intent_cues.build(doc, "thu")
    with pytest.raises(RuleError) as caught:  # cổng của admin: bản nháp bị từ chối trước khi kích hoạt
        config_documents._validate_intent_cues(doc)
    assert caught.value.code == "invalid_document"


def test_the_period_cases_fixture_passes_with_the_default_config():
    from app.services import config_documents

    cases = [(text, expected) for text, expected in config_documents._eval_cases() if expected.startswith(config_documents.PERIOD_PREFIX)]
    assert len(cases) >= 20
    got = config_documents._run_cases(cases)
    assert [(text, expected, got[text]) for text, expected in cases if got[text] != expected] == []


def test_the_activation_gate_sees_a_period_pattern_that_was_broken():
    from app.services import config_documents, intent_cues

    doc = _doc_with("period_day", "(?P<day>", "(?P<day>9999")  # vẫn biên dịch được, đủ nhóm, nhưng không còn khớp ngày nào
    result = config_documents._preview_intent_cues(doc)
    assert result["available"] and result["delta"] < 0
    assert intent_cues.current() is not None and parse("ngày 15/3 tôi chi bao nhiêu").query.period_label == "ngày 15/3"  # xem trước không để lại bản thử


def test_changing_the_words_inside_a_group_changes_the_meaning_without_touching_code():
    from app.services import intent_cues

    doc = _doc_with("period_quarter", "(?P<previous>truoc)", "(?P<previous>truoc|vua roi)")
    intent_cues.apply(intent_cues.build(doc, "thu"))
    try:
        assert parse("quý vừa rồi tôi tiêu bao nhiêu").query.period_label == "quý 3"
    finally:
        intent_cues.apply(intent_cues.CUES)
