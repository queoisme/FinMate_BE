"""Củ (khoai môn, củ cải) không phải tiền, nhưng "1 củ từ ví" là tiền.

Cơ chế loại "củ cải/củ từ" nằm trong thay đổi chưa commit của amount_normalizer (is_money với not_followed_by); ở HEAD chưa có nên các test này
chỉ xanh khi thay đổi đó đã vào. Phần "từ" làm giới từ nằm ở test_chat_parser_regressions.py và xanh ở HEAD.
"""

import pytest

from app.services.amount_normalizer import parse_amount


class TestCuVegetables:
    @pytest.mark.parametrize("text", ["mua 2 củ từ", "mua 2 củ từ luộc ăn"])
    def test_taro_is_still_not_money(self, text):
        assert parse_amount(text) is None

    def test_taro_with_a_price_reads_only_the_price(self):
        assert parse_amount("mua 2 củ từ 30k").value == 30_000

    def test_other_vegetables_unchanged(self):
        assert parse_amount("mua 2 củ cải") is None
