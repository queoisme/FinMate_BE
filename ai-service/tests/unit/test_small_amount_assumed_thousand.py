"""Số nhỏ viết bằng chữ, không đơn vị ("ăn phở bốn mươi lăm") là nghìn: 45.000, không phải 45 đồng.

Người Việt nói giá ngắn gọn bỏ đơn vị, và khoản chi dưới 1.000 đồng gần như không tồn tại. Nhưng đây là ĐOÁN đơn vị nên không bao giờ được tự lưu:
người dùng phải xác nhận. Trước đây "bốn mươi lăm" ra 45 đồng VÀ được phép tự lưu.
"""

from datetime import UTC, datetime

import pytest

from app.services.amount_normalizer import parse_amount
from app.services.transaction_parser import parse_chat

NOW = datetime(2026, 10, 15, 10, 0, tzinfo=UTC)


def entries(text):
    parsed = parse_chat(text, None, now=NOW)
    return parsed, list(getattr(parsed, "entries", None) or ([parsed.entry] if parsed.entry else []))


@pytest.mark.parametrize(
    ("text", "vnd"),
    [
        ("ăn phở bốn mươi lăm", 45_000),
        ("ăn sáng năm mươi", 50_000),
        ("cà phê hai mươi lăm", 25_000),
        ("mua áo một trăm năm mươi", 150_000),
        ("ăn phở năm trăm", 500_000),
    ],
)
def test_a_small_spoken_expense_without_a_unit_is_thousands_and_needs_confirmation(text, vnd):
    _, found = entries(text)
    (entry,) = found
    assert (entry.kind, entry.amount) == ("expense", vnd)
    assert entry.auto_save_ok is False  # đoán đơn vị thì phải hỏi


@pytest.mark.parametrize("text", ["ăn phở bốn mươi lăm nghìn", "ăn phở 45k", "ăn phở bốn mươi lăm ca"])
def test_an_explicit_unit_is_unchanged_and_may_still_auto_save(text):
    _, (entry,) = entries(text)
    assert entry.amount == 45_000
    assert entry.auto_save_ok is True


def test_an_explicit_dong_unit_is_taken_literally():
    _, (entry,) = entries("ăn phở bốn mươi lăm đồng")
    assert entry.amount == 45


def test_the_second_half_of_the_users_sentence_is_50k_not_50_dong():
    # câu gốc: "...hết ba mươi nghìn và tôi đi ăn hết năm mươi": vế sau không đơn vị. Đọc từng vế (hai khoản cùng danh mục có thể bị gộp thành một).
    _, (entry,) = entries("tôi đi ăn hết năm mươi")
    assert (entry.amount, entry.category, entry.auto_save_ok) == (50_000, "food", False)
    _, (entry,) = entries("tôi mua hai lạng thịt lợn hết ba mươi nghìn")
    assert entry.amount == 30_000


def test_a_percentage_is_not_an_amount():
    parsed, found = entries("giảm năm mươi phần trăm")
    assert parse_amount("giảm năm mươi phần trăm") is None
    assert not any(e.kind == "expense" and e.amount for e in found)


def test_a_count_of_people_or_things_is_not_scaled():
    for text in ("mời hai mươi người", "đi hai mươi ngày"):
        _, found = entries(text)
        assert all(e.amount != 20_000 for e in found), text


def test_a_percentage_is_not_an_amount_even_when_rasa_cuts_the_number_out_of_it():
    # Rasa trả thực thể amount = "năm mươi" (mất chữ "phần trăm" phía sau); đường dự phòng đọc cụm đó không được biến nó thành khoản chi
    nlu = {"intent": "add_expense", "confidence": 0.9, "entities": {"amount": "năm mươi"}}
    parsed = parse_chat("giảm năm mươi phần trăm", nlu, now=NOW)
    found = list(getattr(parsed, "entries", None) or ([parsed.entry] if parsed.entry else []))
    assert not any(e.amount for e in found)
