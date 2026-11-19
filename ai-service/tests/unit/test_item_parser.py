"""Câu tạo ngân sách / khoản định kỳ / vay nợ bằng chat (app/services/item_parser.py)."""

from datetime import date, datetime
from zoneinfo import ZoneInfo

import pytest

from app.services import transaction_parser as tp
from app.services.item_parser import BUDGET, LOAN, RECURRING, parse_item

NOW = datetime(2026, 11, 20, 10, 0, tzinfo=ZoneInfo("Asia/Ho_Chi_Minh"))


def parse(text):
    return parse_item(text, NOW, tp.category_from_text)


def test_recurring_with_everything_is_a_draft():
    draft = parse("gia hạn gói Claude Pro 600k hằng tháng ngày 5").draft

    assert (draft.kind, draft.amount, draft.name, draft.cadence, draft.next_due) == (RECURRING, 600_000, "Claude Pro", "monthly", date(2026, 12, 5))


@pytest.mark.parametrize(
    ("text", "asks"),
    [
        ("gia hạn Claude Pro hằng tháng", "bao nhiêu tiền"),
        ("gia hạn Claude Pro 600k", "lặp lại bao lâu"),
        ("gia hạn gói Claude Pro hằng tháng 600k", "ngày nào"),
    ],
)
def test_recurring_asks_for_the_missing_piece(text, asks):
    result = parse(text)

    assert result.draft is None
    assert asks in result.reply


def test_a_follow_up_answer_joined_to_the_question_completes_it():
    first = parse("gia hạn gói Claude Pro hằng tháng 600k")
    joined = parse("gia hạn gói Claude Pro hằng tháng 600k, ngày 25")

    assert first.draft is None
    assert joined.draft.next_due == date(2026, 11, 25)  # ngày 25 chưa qua: tháng này


def test_day_already_passed_rolls_to_next_month_and_slash_form_and_cadence_slash():
    assert parse("đăng ký Netflix 260k hằng tháng ngày 5").draft.next_due == date(2026, 12, 5)
    draft = parse("Netflix 260k/tháng đăng ký ngày 12/12").draft
    assert (draft.cadence, draft.next_due) == ("monthly", date(2026, 12, 12))


def test_cadence_alone_is_not_recurring_but_cadence_with_a_pay_verb_is():
    assert parse("hằng tháng tôi ăn ngoài 3 triệu") is None
    assert parse("đóng tiền net hằng tháng 200k ngày 10").draft.kind == RECURRING


def test_budget_needs_category_and_amount():
    assert parse("đặt ngân sách ăn uống 2 triệu").draft == parse("tạo ngân sách ăn uống 2 triệu").draft
    assert parse("đặt ngân sách ăn uống 2 triệu").draft.kind == BUDGET
    assert "nhóm chi tiêu" in parse("đặt ngân sách 2 triệu").reply
    assert "bao nhiêu" in parse("đặt ngân sách ăn uống").reply


def test_a_budget_question_is_not_a_budget_command():
    assert parse("ngân sách còn bao nhiêu") is None
    assert parse("ăn trưa 50k") is None


@pytest.mark.parametrize(
    ("text", "direction", "who"),
    [
        ("bạn An nợ tôi 700k", "lend", "Bạn An"),
        ("hôm qua An nợ tôi 700k", "lend", "An"),
        ("tôi nợ bạn Bình 2 triệu", "borrow", "Bạn Bình"),
    ],
)
def test_loan_direction_and_counterparty(text, direction, who):
    draft = parse(text).draft

    assert (draft.kind, draft.direction, draft.counterparty) == (LOAN, direction, who)


def test_loan_asks_when_a_piece_is_missing_and_never_guesses_who():
    assert "bao nhiêu" in parse("tôi nợ An").reply
    assert parse("tôi nợ 700k") is None or "với ai" in parse("tôi nợ 700k").reply


def test_money_changing_hands_and_repayments_are_not_new_debts():
    for text in ("trả nợ An 700k", "bạn trả nợ tôi 500k", "cho bạn vay 200k", "bạn cho tôi mượn 300k", "em muốn mua xe máy 40tr"):
        assert parse(text) is None, text
    assert parse("các khoản định kỳ của tôi") is None


def test_parse_chat_routes_to_create_item_and_asks_back():
    done = tp.parse_chat("gia hạn gói Claude Pro 600k hằng tháng ngày 5", now=NOW)
    asking = tp.parse_chat("gia hạn Claude Pro 600k", now=NOW)

    assert done.intent == "create_item" and done.item.kind == RECURRING
    assert asking.intent == "create_item" and asking.item is None and asking.reply_text
