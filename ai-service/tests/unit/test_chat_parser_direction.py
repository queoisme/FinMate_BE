"""Rasa đảo chiều thu / chi / chuyển tiền mà vẫn báo độ chắc cao (scorecard 2026-10-19): luật phải giữ đúng chiều."""

from datetime import datetime

import pytest

from app.services.transaction_parser import VN, parse_chat

NOW = datetime(2026, 10, 19, 10, 0, tzinfo=VN)


def rasa(intent: str, confidence: float = 1.0) -> dict:
    return {"intent": intent, "confidence": confidence, "entities": {}}


@pytest.mark.parametrize("text,nlu_intent,confidence,expected", [
    # người khác gửi/cho mình: tiền vào
    ("ba gửi cho 2 triệu", "transfer_money", 1.0, "add_income"),
    ("chị gửi về 2 triệu", "transfer_money", 0.97, "add_income"),
    ("ba cho 1 triệu đóng học", "add_expense", 1.0, "add_income"),
    ("tiền lãi tiết kiệm 85k", "transfer_money", 0.85, "add_income"),
    ("tiền lãi tiết kiệm 85k", "add_expense", 1.0, "add_income"),  # Rasa v8 thật trả chi tiêu với độ chắc 1,00
    # phí là chi tiêu dù câu có chữ rút/chuyển
    ("phí rút tiền atm 3k", "transfer_money", 1.0, "add_expense"),
    ("thẻ gym tháng 500k", "add_income", 1.0, "add_expense"),
    # rút / nạp vào ví của mình: chuyển tiền
    ("nạp 400k vào ví grab", "add_expense", 1.0, "transfer_money"),
    ("rút tiền 500k", "add_income", 1.0, "transfer_money"),
    # "gửi xe" là chi tiêu, không phải ngoài chủ đề
    ("gửi xe máy 3k", "out_of_scope", 1.0, "add_expense"),
    # không phải lệnh hủy khoản đang chờ
    ("xóa dữ liệu", "cancel", 0.999, "out_of_scope"),
])
def test_rasa_overconfident_direction_is_overruled(text, nlu_intent, confidence, expected):
    assert parse_chat(text, rasa(nlu_intent, confidence), now=NOW).intent == expected


@pytest.mark.parametrize("text,nlu_intent,expected", [
    ("trả tiền lãi vay 2 triệu", "add_expense", "add_expense"),  # trả lãi là chi, không phải tiền lãi về
    ("mẹ cho 500k", "add_income", "add_income"),
    ("cho mẹ 500k", "add_income", "add_expense"),
    ("rút 2 triệu tiền lương", "add_income", "add_income"),  # có dấu hiệu tiền vào rõ: giữ nguyên
    ("chuyển khoản 500k cho mẹ", "transfer_money", "transfer_money"),
    ("đi gửi xe 5k", "add_expense", "add_expense"),
])
def test_correct_direction_is_not_broken(text, nlu_intent, expected):
    assert parse_chat(text, rasa(nlu_intent), now=NOW).intent == expected
