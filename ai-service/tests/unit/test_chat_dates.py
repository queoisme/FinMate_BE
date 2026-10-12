"""Ngày trong câu chat: "hôm qua", "sáng nay", "ngày 5/10". Chỉ nhận ngày không ở tương lai; không chắc thì None (backend dùng giờ hiện tại)."""

from datetime import datetime

import pytest

from app.chat.dates import VN, parse_when, remove_when

NOW = datetime(2026, 10, 12, 10, 0, tzinfo=VN)  # thứ Hai


@pytest.mark.parametrize(
    ("text", "expected"),
    [
        ("ăn phở 45k hôm qua", datetime(2026, 10, 11, 12, 0, tzinfo=VN)),
        ("đổ xăng 50k hôm kia", datetime(2026, 10, 10, 12, 0, tzinfo=VN)),
        ("cà phê 30k hôm nay", NOW),
        ("ăn sáng 25k sáng nay", datetime(2026, 10, 12, 8, 0, tzinfo=VN)),
        ("ăn tối 80k tối qua", datetime(2026, 10, 11, 19, 0, tzinfo=VN)),
        ("an pho 45k hom qua", datetime(2026, 10, 11, 12, 0, tzinfo=VN)),
        ("ăn phở 45k ngày 5/10", datetime(2026, 10, 5, 12, 0, tzinfo=VN)),
        ("ăn phở 45k ngày 5 tháng 10", datetime(2026, 10, 5, 12, 0, tzinfo=VN)),
        ("hôm 5/10 ăn phở 45k", datetime(2026, 10, 5, 12, 0, tzinfo=VN)),
    ],
)
def test_phrases(text, expected):
    found = parse_when(text, NOW)
    assert found is not None and found.value == expected


@pytest.mark.parametrize(
    "text",
    [
        "ăn phở 45k",
        "ăn phở 45k ngày 5/13",  # tháng 13
        "ăn phở 45k ngày 32/1",  # ngày 32
        "ăn phở 45k ngày 20/12",  # chưa tới
        "chiều nay ăn phở 45k",  # 17:00 chưa tới lúc 10:00
        "mua giày giảm 20/10 600k",  # không có chữ "ngày"/"hôm" đứng trước
    ],
)
def test_no_date(text):
    assert parse_when(text, NOW) is None


def test_phrase_is_removed_from_the_sentence():
    text = "ăn phở 45k hôm qua"
    assert remove_when(text, parse_when(text, NOW)) == "ăn phở 45k"


def test_pronoun_toi_is_not_the_evening():
    assert parse_when("tôi qua quán ăn 50k", NOW) is None  # "tôi qua" = tôi + qua (đi qua), không phải "tối qua"
    assert parse_when("tôi nay ăn phở 45k", NOW) is None


def test_accented_toi_still_means_evening():
    assert parse_when("ăn tối 80k tối qua", NOW).value == datetime(2026, 10, 11, 19, 0, tzinfo=VN)
