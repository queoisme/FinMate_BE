"""Câu xã giao bị Rasa hiểu nhầm (đo trên Rasa thật 2026-10-21): "ăn cơm chưa" thành chi tiêu thiếu số tiền, "đi ăn không" thành từ chối,
"chào bạn" thành ngoài chủ đề, "cảm ơn" thành xác nhận, "giá bitcoin bao nhiêu" thành hỏi tổng chi tiêu."""

from datetime import datetime

import pytest

from app.services.transaction_parser import VN, parse_chat

NOW = datetime(2026, 10, 21, 10, 0, tzinfo=VN)


def rasa(intent: str, confidence: float = 1.0) -> dict:
    return {"intent": intent, "confidence": confidence, "entities": {}}


@pytest.mark.parametrize("text,nlu_intent,confidence,expected", [
    ("ăn cơm chưa", "add_expense", 1.0, "out_of_scope"),
    ("ăn cơm chưa?", "add_expense", 1.0, "out_of_scope"),
    ("bạn ăn cơm chưa", "deny", 0.91, "out_of_scope"),
    ("đi ăn không", "deny", 0.92, "out_of_scope"),
    ("uống cà phê không", "deny", 0.97, "out_of_scope"),
    ("đi chơi không", "deny", 0.98, "out_of_scope"),
    ("ngủ chưa", "deny", 0.89, "out_of_scope"),
    ("chào bạn", "out_of_scope", 0.94, "greet"),
    ("cảm ơn", "confirm", 0.95, "thanks"),
    ("cảm ơn bạn", "confirm", 0.9, "thanks"),
    ("giá bitcoin bao nhiêu", "ask_spending", 1.0, "out_of_scope"),
    ("giá xăng hôm nay bao nhiêu", "ask_spending", 1.0, "out_of_scope"),
])
def test_small_talk_is_not_a_transaction_or_a_reply(text, nlu_intent, confidence, expected):
    assert parse_chat(text, rasa(nlu_intent, confidence), now=NOW).intent == expected


@pytest.mark.parametrize("text,nlu_intent,expected", [
    ("không", "deny", "deny"),
    ("không phải", "deny", "deny"),
    ("sai rồi", "deny", "deny"),
    ("chưa đúng", "deny", "deny"),
    ("không, là mua sắm", "deny", "deny"),
    ("đúng rồi", "confirm", "confirm"),
    ("hủy", "cancel", "cancel"),
    ("ăn cơm", "add_expense", "needs_amount"),
    ("mua nước", "add_expense", "needs_amount"),
    ("ăn phở 45k", "add_expense", "add_expense"),
    ("tháng này tôi tiêu bao nhiêu", "ask_spending", "ask_spending"),
    ("tháng này giá xăng tôi chi bao nhiêu", "ask_spending", "ask_spending"),  # có từ về tiền của mình: vẫn là hỏi chi tiêu
    ("chào", "greet", "greet"),
    ("tạm biệt", "goodbye", "goodbye"),
])
def test_real_replies_and_transactions_are_not_broken(text, nlu_intent, expected):
    assert parse_chat(text, rasa(nlu_intent), now=NOW).intent == expected
