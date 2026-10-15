"""Hai luật sai ở chat khi người dùng không đồng ý AI: "cây lau nhà" ra Ăn uống ("lau" bị đọc là lẩu) và "thước kẻ" ra Sức khỏe ("mua thuốc" dài hơn "thước kẻ")."""

from datetime import UTC, datetime

import pytest

from app.services.category_resolver import resolve
from app.services.transaction_parser import parse_chat

NOW = datetime(2026, 10, 15, 10, 0, tzinfo=UTC)


def category(text: str) -> str | None:
    return parse_chat(text, None, now=NOW).entry.category


@pytest.mark.parametrize(
    ("text", "slug"),
    [
        ("mua cây lau nhà 80k", "shopping"),
        ("mua nước lau sàn 50k", "shopping"),
        ("mua khăn lau 20k", "shopping"),
        ("mua cay lau nha 80k", "shopping"),  # gõ không dấu
        ("mua thước kẻ 5k", "education"),
        ("mua thuoc ke 5k", "education"),
    ],
)
def test_the_two_wrong_rules_are_fixed(text, slug):
    assert category(text) == slug


@pytest.mark.parametrize(
    ("text", "slug"),
    [
        ("ăn lẩu 200k", "food"),
        ("ăn lau 200k", "food"),  # lẩu gõ không dấu vẫn là đồ ăn
        ("mua thuốc hạ sốt 20k", "health"),
        ("mua thuoc ho 30k", "health"),
        ("mua thuốc 50k", "health"),
    ],
)
def test_lau_and_thuoc_keep_meaning_what_they_meant(text, slug):
    assert category(text) == slug


def test_the_resolver_agrees_on_the_bare_phrases():
    assert resolve("cây lau nhà", chat=True).slug == "shopping"
    assert resolve("mua thước kẻ", chat=True).slug == "education"


# "mua đồ" (shopping, 6 ký tự) dài hơn "đồ ăn" (food, 5 ký tự): "mua đồ ăn" ra Mua sắm. Cùng loại với "mua thước kẻ"; phép quét 438 từ khóa x 6 động từ thấy đây là ca phổ biến nhất.
@pytest.mark.parametrize(
    ("text", "slug"),
    [
        ("mua đồ ăn 50k", "food"),
        ("mua đồ ăn vặt 30k", "food"),
        ("mua đồ ăn sáng 25k", "food"),
        ("mua đồ uống 40k", "food"),
        ("mua do an 50k", "food"),  # gõ không dấu
        ("mua thêm đồ ăn 60k", "food"),
    ],
)
def test_buying_food_or_drinks_is_food(text, slug):
    assert category(text) == slug


@pytest.mark.parametrize(
    ("text", "slug"),
    [
        ("mua đồ 100k", "shopping"),  # "mua đồ" không nói rõ vẫn là mua sắm
        ("mua đồ chơi cho bé 120k", "family"),
        ("mua đồ gia dụng 200k", "shopping"),
        ("ăn đồ ăn nhanh 70k", "food"),
    ],
)
def test_the_food_fix_does_not_change_the_other_do_phrases(text, slug):
    assert category(text) == slug
