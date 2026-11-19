"""Chiều giao dịch (chi / thu / chuyển) và câu "định mua": các LOẠI lỗi đo được ngày 2026-11-18 trên bộ heldout + coverage.

Mỗi nhóm là một loại dấu hiệu trong app/data/intent_cues.json, không phải một câu lẻ; các câu "phải giữ nguyên" canh việc sửa không phá chỗ khác.
"""

from datetime import UTC, datetime

import pytest

from app.services.transaction_parser import parse_chat

NOW = datetime(2026, 11, 18, 10, 0, tzinfo=UTC)


@pytest.mark.parametrize(
    ("text", "intent"),
    [
        # "anh" trong "tiếng anh" là ngoại ngữ, không phải người cho tiền; "bánh cho em" cũng không khớp "anh cho em" (thiếu ranh giới từ)
        ("mua bánh cho em 50k", "add_expense"),
        ("đóng tiền học thêm tiếng anh cho em 900k", "add_expense"),
        ("dong tien hoc them tieng anh cho em 900k", "add_expense"),
        # gửi cho người thân là chuyển tiền
        ("gửi mẹ 2 triệu", "transfer_money"),
        # nạp vào game / điện thoại là chi tiêu, không phải chuyển ví
        ("nạp tiền vào game 100k", "add_expense"),
        # tiền lãi ngân hàng trả, cashback, tiền từ khách: tiền vào
        ("lãi suất tiết kiệm tháng này về 65k", "add_income"),
        ("ngân hàng trả lãi quý này 85k", "add_income"),
        ("cashback thẻ tín dụng 45k", "add_income"),
        ("chuyển khoản tiền hàng từ khách 2tr", "add_income"),
        ("bạn chuyển trả tiền cơm 150k", "add_income"),
        # nói định mua kèm giá là hỏi mua, không ghi chi tiêu
        ("em muốn mua xe máy 40tr", "evaluate_purchase"),
        ("định mua laptop 20tr", "evaluate_purchase"),
    ],
)
def test_direction_errors_found_in_the_measurement(text, intent):
    assert parse_chat(text, None, now=NOW).intent == intent


@pytest.mark.parametrize(
    ("text", "intent"),
    [
        ("gửi xe 5k", "add_expense"),  # "gửi" + không phải người thân
        ("nạp 300k vào momo", "transfer_money"),  # nạp vào ví của mình vẫn là chuyển
        ("nạp tiền vào ví 500k", "transfer_money"),
        ("trả lãi vay ngân hàng 2tr", "add_expense"),  # trả lãi cho ngân hàng là chi
        ("cho em 100k", "add_expense"),
        ("anh cho em 100k", "add_income"),  # người dùng xưng em: anh cho mình
        ("mua cà phê 45k", "add_expense"),
        ("muốn mua gì cho bữa tối", "out_of_scope"),  # không có giá: không phải câu hỏi mua có giá
    ],
)
def test_neighbouring_sentences_keep_their_intent(text, intent):
    assert parse_chat(text, None, now=NOW).intent == intent
