"""Rasa cũng có thể đoán ``evaluate_purchase``: lá phiếu đó không bao giờ tự tạo bản nháp (giá và ngày chỉ do purchase_parser đọc), không bao giờ làm câu ngoài phạm vi
mang nhãn ``evaluate_purchase`` kèm lời "ngoài phạm vi", và khi bất đồng với luật thì luật quyết, câu được đánh dấu để duyệt.

"Hôm nay" cố định là thứ Bảy 17/10/2026 15:30 giờ Việt Nam.
"""

from datetime import date, datetime

import pytest

from app.services import intent_cues
from app.services.purchase_parser import PurchaseDraft
from app.services.transaction_parser import VN, parse_chat

NOW = datetime(2026, 10, 17, 15, 30, tzinfo=VN)
MIN = intent_cues.CUES.thresholds["min_nlu_confidence"]
CONFIDENCES = (MIN, 0.8, 0.99)  # từ ngưỡng nhận lời Rasa tới rất chắc


def rasa(confidence, intent="evaluate_purchase", **entities):
    return {"intent": intent, "confidence": confidence, "entities": entities}


def replies():
    return intent_cues.current().replies


@pytest.mark.parametrize("confidence", CONFIDENCES)
def test_a_valid_draft_comes_from_the_rules_whatever_rasa_says(confidence):
    parsed = parse_chat("Hôm nay mua cái quần 800.000đ được không?", rasa(confidence), now=NOW)
    assert parsed.intent == "evaluate_purchase" and parsed.entries == ()
    assert parsed.purchase == PurchaseDraft("quần", 800_000, date(2026, 10, 17), None)
    assert (parsed.nlu_source, parsed.nlu_intent) == ("rasa", "evaluate_purchase") and parsed.unclear_reason is None


@pytest.mark.parametrize("confidence", CONFIDENCES)
def test_rasa_cannot_supply_the_price_or_create_a_draft_on_its_own(confidence):
    # luật không đọc ra cách hỏi: Rasa có cắt được số tiền cũng không thành bản nháp, câu vẫn là khoản chi
    parsed = parse_chat("mua cái quần 800k", rasa(confidence, amount="800k"), now=NOW)
    assert parsed.intent == "add_expense" and parsed.purchase is None and parsed.entry.amount == 800_000
    # có cách hỏi mà luật không thấy giá: hỏi lại giá, không dùng thực thể của Rasa
    parsed = parse_chat("mua laptop được không?", rasa(confidence, amount="20 triệu"), now=NOW)
    assert parsed.intent == "needs_amount" and parsed.purchase is None and parsed.reply_text == replies()["needs_amount"]


@pytest.mark.parametrize("confidence", CONFIDENCES)
def test_an_expense_sentence_stays_an_expense_and_is_flagged_for_review(confidence):
    parsed = parse_chat("mua cà phê 45k", rasa(confidence), now=NOW)
    assert parsed.intent == "add_expense" and parsed.purchase is None
    assert (parsed.entry.kind, parsed.entry.amount) == ("expense", 45_000)
    assert parsed.unclear_reason == "disagree"  # để câu có thể vào hàng chờ nlu_candidates
    assert parsed.entry.auto_save_ok is False  # bất đồng nên không tự ghi


def test_below_rasas_acceptance_threshold_the_vote_is_ignored_like_any_other():
    parsed = parse_chat("mua cà phê 45k", rasa(MIN - 0.01), now=NOW)
    assert (parsed.intent, parsed.nlu_source, parsed.unclear_reason) == ("add_expense", "rules", None)


@pytest.mark.parametrize("confidence", CONFIDENCES)
@pytest.mark.parametrize("text", ["nên mua nhà hay thuê nhà", "có nên mua vàng lúc này không", "iphone 20 triệu có đáng mua không"])
def test_off_topic_advice_stays_out_of_scope_with_its_own_reply(text, confidence):
    parsed = parse_chat(text, rasa(confidence), now=NOW)
    assert parsed.intent == "out_of_scope" and parsed.purchase is None and parsed.reply_text == replies()["out_of_scope"]


@pytest.mark.parametrize("confidence", CONFIDENCES)
@pytest.mark.parametrize(
    ("text", "intent"),
    [("chào bạn", "greet"), ("cảm ơn", "thanks"), ("tháng này tôi tiêu bao nhiêu", "ask_spending"), ("tháng này tôi mua quần hết bao nhiêu được không", "ask_spending"), ("ăn cơm", "needs_amount")],
)
def test_other_sentences_follow_the_rules_when_rasa_says_purchase(text, intent, confidence):
    assert parse_chat(text, rasa(confidence), now=NOW).intent == intent


@pytest.mark.parametrize("confidence", CONFIDENCES)
@pytest.mark.parametrize(
    "text",
    [
        "Hôm nay mua cái quần 800.000đ được không?", "mua laptop được không?", "mua cà phê 45k", "nên mua nhà hay thuê nhà", "hôm qua mua quần 800k được không",
        "ngày 31/2 mua laptop 20 triệu được không", "mua quần tám trăm được không", "đặt mục tiêu mua laptop 25 triệu", "xin chào", "250k", "",
    ],
)
def test_the_label_evaluate_purchase_always_comes_with_a_draft_or_a_follow_up_question(text, confidence):
    parsed = parse_chat(text, rasa(confidence), now=NOW)
    if parsed.intent == "evaluate_purchase":
        assert parsed.purchase is not None or parsed.reply_text not in (None, replies()["out_of_scope"])
        assert parsed.reply_text != replies()["out_of_scope"]
    else:
        assert parsed.purchase is None


def test_the_response_never_pairs_the_label_with_the_out_of_scope_reply():
    # đường dẫn trước khi sửa: Rasa nói evaluate_purchase, parse_purchase None -> nhãn evaluate_purchase kèm lời "ngoài phạm vi"
    parsed = parse_chat("nên mua nhà hay thuê nhà", rasa(0.99), now=NOW)
    assert parsed.intent != "evaluate_purchase"
