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
