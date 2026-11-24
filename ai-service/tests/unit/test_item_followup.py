"""Câu nối tiếp việc VỪA làm (ngân sách / khoản định kỳ / vay nợ), nhờ app gửi kèm ngữ cảnh (app/services/item_parser.py)."""

from datetime import datetime
from zoneinfo import ZoneInfo

import pytest

from app.services import transaction_parser as tp
from app.services.item_parser import BUDGET, LOAN, RECURRING, ItemContext, parse_item

NOW = datetime(2026, 11, 20, 10, 0, tzinfo=ZoneInfo("Asia/Ho_Chi_Minh"))
BUDGET_LAST = ItemContext(BUDGET, 2_000_000, name="Ăn uống", category="dining")
LOAN_LAST = ItemContext(LOAN, 700_000, name="Cho Bạn An vay", direction="lend", counterparty="Bạn An")
RECURRING_LAST = ItemContext(RECURRING, 600_000, name="Claude Pro", cadence="monthly")


def parse(text, last):
    return parse_item(text, NOW, tp.category_from_text, last)


@pytest.mark.parametrize("text", ["đổi thành 3 triệu", "tăng lên 3 triệu", "sửa lại 3 triệu", "giảm xuống 3 triệu"])
def test_changing_the_amount_of_what_was_just_made(text):
    draft = parse(text, BUDGET_LAST).draft

    assert (draft.operation, draft.kind, draft.amount, draft.category, draft.name) == ("update", BUDGET, 3_000_000, "dining", "Ăn uống")


def test_adding_to_the_amount_and_the_target_stays_the_same_person():
    draft = parse("thêm 300k nữa", LOAN_LAST).draft

    assert (draft.operation, draft.amount, draft.counterparty, draft.direction) == ("add", 300_000, "Bạn An", "lend")


@pytest.mark.parametrize("text", ["xóa cái đó", "hủy khoản vừa rồi", "bỏ ngân sách đó đi"])
def test_deleting_the_last_thing_needs_a_delete_verb_and_a_referent(text):
    draft = parse(text, BUDGET_LAST).draft

    assert (draft.operation, draft.kind, draft.category) == ("delete", BUDGET, "dining")


def test_a_bare_cancel_or_a_delete_without_context_is_left_to_the_other_rules():
    assert parse("hủy", BUDGET_LAST) is None
    assert parse("xóa cái đó", None) is None
    assert parse("đổi thành 3 triệu", None) is None


def test_doing_another_one_of_the_same_kind():
    budget = parse("còn di chuyển thì 1 triệu", BUDGET_LAST).draft
    assert (budget.operation, budget.kind, budget.amount, budget.name) == ("create", BUDGET, 1_000_000, "Di chuyển")

    recurring = parse("còn Netflix 260k ngày 12", RECURRING_LAST).draft
    assert (recurring.kind, recurring.name, recurring.cadence, recurring.amount) == (RECURRING, "Netflix", "monthly", 260_000)
    assert "ngày nào" in parse("còn Netflix 260k", RECURRING_LAST).reply


def test_an_amount_with_other_words_is_not_a_follow_up():
    assert parse("tăng lương lên 20 triệu", BUDGET_LAST) is None
    assert parse("thêm 50k tiền cà phê", BUDGET_LAST) is None


def test_parse_chat_uses_the_context_only_when_given():
    with_context = tp.parse_chat("đổi thành 3 triệu", now=NOW, item_context=BUDGET_LAST)
    without = tp.parse_chat("đổi thành 3 triệu", now=NOW)

    assert with_context.intent == "create_item" and with_context.item.operation == "update"
    assert without.intent != "create_item"


# --- sửa / xóa theo tên, thu nhập định kỳ, ngân sách tổng


def named(text):
    return parse_item(text, NOW, tp.category_from_text)


@pytest.mark.parametrize(
    ("text", "kind", "operation", "name", "amount"),
    [
        ("xóa ngân sách ăn uống", "budget", "delete_named", "Ăn uống", 0),
        ("xóa khoản nợ của An", "loan", "delete_named", "An", 0),
        ("xóa gói Netflix", "recurring", "delete_named", "Netflix", 0),
        ("đổi khoản Netflix thành 300k", "any", "update_named", "Netflix", 300_000),
        ("tăng ngân sách ăn uống lên 3 triệu", "budget", "update_named", "Ăn uống", 3_000_000),
    ],
)
def test_editing_or_deleting_an_existing_item_by_name(text, kind, operation, name, amount):
    draft = named(text).draft

    assert (draft.kind, draft.operation, draft.name, draft.amount) == (kind, operation, name, amount)


@pytest.mark.parametrize("text", ["xóa giao dịch cà phê", "hủy mục tiêu mua xe", "đổi 500k tiền ăn", "bỏ 50k vào ví", "xóa cái đó", "đặt ngân sách ăn uống 2 triệu"])
def test_other_deletes_and_edits_are_not_named_item_operations(text):
    result = named(text)

    assert result is None or result.draft.operation == "create"


def test_recurring_income_needs_a_cadence_and_is_marked_inflow():
    salary = named("lương 15 triệu hằng tháng ngày 5").draft
    assert (salary.kind, salary.name, salary.direction, salary.cadence, salary.amount) == ("recurring", "Lương", "inflow", "monthly", 15_000_000)
    assert named("mỗi tháng nhận lương 15tr ngày 5").draft.direction == "inflow"
    assert named("nhận lương 15 triệu") is None  # một khoản thu thường, không phải định kỳ
    assert named("gia hạn gói Claude Pro 600k hằng tháng ngày 5").draft.direction is None  # khoản chi như cũ


@pytest.mark.parametrize("text", ["đặt ngân sách tổng 15 triệu", "ngân sách cả tháng 15 triệu"])
def test_an_overall_budget_has_no_category(text):
    draft = named(text).draft

    assert (draft.kind, draft.amount, draft.category) == ("budget_total", 15_000_000, None)


@pytest.mark.parametrize(
    "text",
    ["vì sao ô số dư ra 14,5 triệu?", "số này tính thế nào", "con số đó từ đâu ra vậy", "giải thích số liệu giúp mình", "tại sao số cần góp lại là 2,7 triệu", "căn cứ vào đâu mà kết luận vậy"],
)
def test_asking_to_explain_the_numbers_of_the_last_answer(text):
    assert tp.parse_chat(text, now=NOW).intent == "explain_numbers"


@pytest.mark.parametrize("text", ["ăn trưa 50k", "tháng này tôi tiêu bao nhiêu", "đặt ngân sách ăn uống 2 triệu", "mua xe 600 triệu sau 2 năm được không", "vì sao tôi tiêu nhiều thế"])
def test_other_sentences_are_not_explain_requests(text):
    assert tp.parse_chat(text, now=NOW).intent != "explain_numbers"
