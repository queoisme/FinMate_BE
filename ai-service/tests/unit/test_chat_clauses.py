"""Tách một câu chat thành nhiều khoản: chỉ tách khi CẢ HAI phía của dấu phân cách đều có số tiền."""

import pytest

from app.chat.clauses import split_clauses


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
