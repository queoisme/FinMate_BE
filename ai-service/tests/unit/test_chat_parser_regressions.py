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


class TestDisagreementNeverAutoSaves:
    """Rasa chắc 1,0 và thắng luật khi bất đồng loại giao dịch; nhưng luật vừa bất đồng thì không được ghi thẳng không hỏi."""

    def test_wallet_topup_misread_as_expense_is_not_auto_saved(self):
        parsed = parse_chat("nạp 400k vào ví grab", rasa("add_expense"), now=NOW, auto_save_below=500_000)
        assert parsed.unclear_reason == "disagree"
        assert parsed.entry.auto_save_ok is False

    def test_agreement_still_auto_saves(self):
        parsed = parse_chat("ăn phở 45k", rasa("add_expense"), now=NOW, auto_save_below=500_000)
        assert parsed.unclear_reason is None
        assert parsed.entry.auto_save_ok is True


class TestGapsRasaOverconfidenceExposed:
    """Rasa chắc ~1,0 nhưng sai ở khung câu chưa thấy; luật phải đỡ được."""

    @pytest.mark.parametrize("text", ["tính giúp tổng tiền tuần trước", "tinh giup tong tien 3 ngay qua", "tổng tiền sáng nay"])
    def test_total_money_question_is_a_spending_query(self, text):
        assert parse_chat(text, rasa("add_expense"), now=NOW).intent == "ask_spending"
        assert parse_chat(text, None, now=NOW).intent == "ask_spending"

    def test_total_marker_with_an_amount_is_still_an_expense(self):
        assert parse_chat("ăn trưa 50k tổng tiền 50k", None, now=NOW).intent == "add_expense"

    def test_unaccented_salary_is_income_for_rules_and_unsure_rasa(self):
        assert parse_chat("95.000 tien luong ve", None, now=NOW).intent == "add_income"
        assert parse_chat("95.000 tien luong ve", rasa("add_expense", 0.9), now=NOW).intent == "add_income"

    def test_unaccented_salary_with_saturated_rasa_is_flagged_not_silent(self):
        parsed = parse_chat("95.000 tien luong ve", rasa("add_expense", 0.997), now=NOW, auto_save_below=500_000)
        assert parsed.unclear_reason == "disagree"
        assert parsed.entry.auto_save_ok is False

    def test_paying_someone_salary_is_not_income(self):
        assert parse_chat("tra tien luong nhan vien 5 trieu", None, now=NOW).intent == "add_expense"


class TestAmbiguousSendNeverAutoSaves:
    """"gửi tiền" mơ hồ chiều tiền ("bạn ở ghép gửi tiền trọ 1tr2" là tiền vào): chưa chắc thì hỏi, không ghi thẳng."""

    @pytest.mark.parametrize("text", ["bạn ở ghép gửi tiền trọ 1tr2", "gửi tiền điện 500k", "ban o ghep gui tien tro 1tr2"])
    def test_gui_is_never_auto_saved(self, text):
        parsed = parse_chat(text, rasa("add_expense"), now=NOW, auto_save_below=2_000_000)
        assert parsed.entry.auto_save_ok is False

    def test_gui_xe_is_still_auto_saved(self):
        parsed = parse_chat("gửi xe 5k", rasa("add_expense"), now=NOW, auto_save_below=2_000_000)
        assert parsed.entry.auto_save_ok is True

