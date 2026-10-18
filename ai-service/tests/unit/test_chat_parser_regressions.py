"""Hai lỗi tìm thấy khi đo Rasa (scorecard, 2026-10-18): "rút 1 củ từ ví" mất số tiền, và "bạn bao nhiêu tuổi" bị hiểu là câu hỏi chi tiêu."""

from datetime import datetime

import pytest

from app.services.amount_normalizer import parse_amount
from app.services.transaction_parser import VN, parse_chat

NOW = datetime(2026, 10, 18, 10, 0, tzinfo=VN)


def rasa(intent: str, confidence: float = 1.0) -> dict:
    return {"intent": intent, "confidence": confidence, "entities": {}}


class TestCuFollowedByFrom:
    """"từ" vừa là giới từ ("rút 1 củ từ ví") vừa đứng sau "củ từ" (khoai môn)."""

    @pytest.mark.parametrize("text,value", [
        ("rút 1 củ từ ví", 1_000_000),
        ("chuyển 2 củ từ ví sang thẻ", 2_000_000),
        ("nhận 3 củ từ mẹ", 3_000_000),
        ("rút 2 củ từ tiết kiệm", 2_000_000),
    ])
    def test_preposition_after_cu_is_still_money(self, text, value):
        match = parse_amount(text)
        assert match is not None and match.value == value

    @pytest.mark.parametrize("text", ["mua 2 củ từ", "mua 2 củ từ luộc ăn"])
    def test_taro_is_still_not_money(self, text):
        assert parse_amount(text) is None

    def test_taro_with_a_price_reads_only_the_price(self):
        assert parse_amount("mua 2 củ từ 30k").value == 30_000

    def test_other_vegetables_unchanged(self):
        assert parse_amount("mua 2 củ cải") is None

    def test_transfer_keeps_its_amount_through_the_chat_path(self):
        parsed = parse_chat("rút 1 củ từ ví", rasa("transfer_money"), now=NOW)
        assert parsed.intent == "transfer_money"
        assert parsed.entry.amount == 1_000_000


class TestSmallTalkIsNotASpendingQuestion:
    @pytest.mark.parametrize("text", ["bạn bao nhiêu tuổi", "ban bao nhieu tuoi", "ô tô ở đâu rồi", "hôm nay trời đẹp quá"])
    def test_rasa_overconfident_ask_spending_is_overruled(self, text):
        assert parse_chat(text, rasa("ask_spending"), now=NOW).intent == "out_of_scope"

    def test_how_old_without_rasa(self):
        assert parse_chat("bạn bao nhiêu tuổi", None, now=NOW).intent == "out_of_scope"

    @pytest.mark.parametrize("text", [
        "tháng này tôi tiêu bao nhiêu",
        "còn bao nhiêu tiền",
        "xem chi tiêu tháng này",
        "chi tiêu hôm nay",
        "tuần này thu chi thế nào",
        "tháng này mình xài hết bao nhiêu",
        "tổng chi tuần trước",
    ])
    def test_real_spending_questions_still_answered(self, text):
        assert parse_chat(text, rasa("ask_spending"), now=NOW).intent == "ask_spending"

    @pytest.mark.parametrize("text", ["tháng này tôi tiêu bao nhiêu", "còn bao nhiêu tiền", "tổng chi tuần trước"])
    def test_real_spending_questions_without_rasa(self, text):
        assert parse_chat(text, None, now=NOW).intent == "ask_spending"
