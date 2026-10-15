"""Số đọc bằng chữ kèm đơn vị "k": người Việt đọc "k" là "ca"/"ka" nên STT trả "năm mươi ca" (= 50k); "năm mươi k" gõ chữ k cũng vậy.

Trước đây cả hai ra 50 đồng vì đơn vị k chỉ được nhận sau chữ số ("50k"), không sau số viết bằng chữ.
"""

from datetime import UTC, datetime

import pytest

from app.services.amount_normalizer import parse_amount, remove_amount
from app.services.category_resolver import resolve
from app.services.transaction_parser import parse_chat

NOW = datetime(2026, 10, 15, 10, 0, tzinfo=UTC)


@pytest.mark.parametrize(
    ("text", "vnd"),
    [
        ("ăn phở năm mươi ca", 50_000),
        ("ăn sáng năm mươi k", 50_000),
        ("cà phê hai mươi lăm ka", 25_000),
        ("mua áo một trăm năm mươi ca", 150_000),
        ("tiền xe ba trăm k", 300_000),
        ("đi ăn hết năm mươi ca rồi về", 50_000),
    ],
)
def test_a_spoken_number_followed_by_k_ca_or_ka_is_thousands(text, vnd):
    assert parse_amount(text).value == vnd


@pytest.mark.parametrize(
    "text",
    ["hai ca làm việc", "ba ca trực đêm", "ăn hết năm mươi cà phê", "năm mươi ca cao", "một ca cấp cứu"],
)
def test_ca_that_is_a_noun_is_not_a_unit(text):
    found = parse_amount(text)
    assert found is None or found.value not in (2_000, 3_000, 1_000, 50_000)


def test_the_unit_word_is_removed_from_the_description_and_the_amount_is_not_50_dong():
    entry = parse_chat("tôi đi ăn hết năm mươi ca", None, now=NOW).entry
    assert (entry.amount, entry.category) == (50_000, "food")
    assert "ca" not in entry.description.split()


def test_a_spoken_amount_with_a_real_unit_is_unchanged():
    assert parse_amount("ăn phở bốn mươi lăm nghìn").value == 45_000
    assert parse_amount("mua hai lạng thịt lợn hết ba mươi nghìn").value == 30_000


def test_pork_in_any_wording_is_food():
    # "thịt heo" có sẵn nhưng "thịt lợn" (cách nói miền Bắc, cũng là chữ STT hay trả) thì không có
    for phrase in ("thịt lợn", "hai lạng thịt lợn", "mua thịt lợn", "thịt heo", "thịt gà", "thịt dê"):
        assert resolve(phrase, chat=True).slug == "food", phrase
    assert parse_chat("mua hai lạng thịt lợn ba mươi nghìn", None, now=NOW).entry.category == "food"
