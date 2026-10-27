"""Tách một câu chat thành nhiều khoản: chỉ tách khi CẢ HAI phía của dấu phân cách đều có số tiền."""

import pytest

from app.services.clauses import split_clauses


@pytest.mark.parametrize(
    ("text", "expected"),
    [
        ("ăn cơm 20k, uống nước 10k, đổ xăng 50k", ["ăn cơm 20k", "uống nước 10k", "đổ xăng 50k"]),
        ("ăn phở 45k và cà phê 30k", ["ăn phở 45k", "cà phê 30k"]),
        ("đi grab 50k rồi ăn trưa 40k", ["đi grab 50k", "ăn trưa 40k"]),
        ("ăn phở 45k + cà phê 30k", ["ăn phở 45k", "cà phê 30k"]),
        ("ăn cơm 20k; uống nước 10k", ["ăn cơm 20k", "uống nước 10k"]),
        ("nhận lương 12 triệu, ăn cơm 20k", ["nhận lương 12 triệu", "ăn cơm 20k"]),
    ],
)
def test_two_amounts_around_a_separator_split(text, expected):
    assert split_clauses(text) == expected


@pytest.mark.parametrize(
    "text",
    [
        "mua đồ 1,5tr",  # dấu phẩy thập phân
        "đi taxi 1.250.000",  # dấu chấm hàng nghìn
        "mua 2 lít sữa 50k",  # số lượng không phải khoản thứ hai
        "ăn cơm 20k, 2 người",  # vế sau không có số tiền
        "mua mì và phở 60k",  # vế trước không có số tiền
        "ăn cơm",
        "",
    ],
)
def test_everything_else_stays_one_clause(text):
    assert split_clauses(text) == [text]


# --- câu thật từ file giọng nói stt-service/Data_Voice (STT trả chữ không có dấu câu) ---
from datetime import datetime  # noqa: E402

from app.services.transaction_parser import VN, parse_chat  # noqa: E402

_NOW = datetime(2026, 10, 14, 15, 30, tzinfo=VN)


def _entries(text):
    return [(e.kind, e.amount, e.category) for e in parse_chat(text, now=_NOW).entries]


def test_two_spoken_amounts_in_a_row_are_two_entries_not_one_merged_amount():
    entries = _entries("hôm qua ăn phở bốn mươi nghìn mua laptop hai mươi ba triệu năm trăm tám triệu rưỡi một tháng tiền thuê nhà")
    assert entries == [("expense", 40_000, "food"), ("expense", 23_500_000, "shopping"), ("expense", 8_500_000, "housing")]


def test_slang_amounts_spoken_as_words_are_not_mistaken_for_volume():
    # "một lít hai" (120.000) và "ba lít" (300.000) là tiền; chỉ "2 lít" viết bằng chữ số mới bị nghi là thể tích.
    amounts = [amount for _, amount, _ in _entries("tiền trọ hai củ mua nhà ba tỏi hai mua sạc một lít hai đi chợ ba lít")]
    assert amounts == [2_000_000, 3_200_000_000, 120_000, 300_000]


def test_digit_amounts_in_a_row_are_still_not_split_and_volume_in_digits_is_still_not_money():
    assert len(parse_chat("mua 2 lít sữa 50k", now=_NOW).entries) == 1
    assert len(parse_chat("ăn 20k 30k", now=_NOW).entries) == 1
