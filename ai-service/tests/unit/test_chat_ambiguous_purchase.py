"""Chat: khoản chi LỚN có động từ mua mà không nói ngày/đã mua ("mua xe 600 triệu") là câu mơ hồ: ghi khoản đã chi hay hỏi có nên mua?

Chat không đoán và không ghi ngay: trả ý định ambiguous_purchase kèm khoản nháp và câu hỏi lại. Ngưỡng và câu hỏi nằm ở app/data/intent_cues.json
(thresholds.ambiguous_purchase_min, replies.ambiguous_purchase), admin sửa được; ở đây chỉ ghim hành vi.
"""

from datetime import UTC, datetime

import pytest

from app.services.transaction_parser import AMBIGUOUS_PURCHASE_INTENT, parse_chat

NOW = datetime(2026, 11, 17, 10, 0, tzinfo=UTC)


@pytest.mark.parametrize("text", ["mua xe 600 triệu", "Mua xe máy 45 triệu", "sắm laptop 25tr", "mua nhà 2 tỷ"])
def test_large_unqualified_purchase_asks_back(text):
    parsed = parse_chat(text, now=NOW)
    assert parsed.intent == AMBIGUOUS_PURCHASE_INTENT
    assert parsed.entry is not None and parsed.entry.kind == "expense"
    assert not parsed.entry.auto_save_ok  # không bao giờ ghi ngay
    assert parsed.reply_text


@pytest.mark.parametrize(
    "text",
    [
        "mua cà phê 45k",  # nhỏ: vẫn là khoản chi
        "mua laptop 15 triệu",  # dưới ngưỡng
        "hôm qua mua xe máy 45 triệu",  # có ngày: đã mua
        "đã mua xe 600 triệu",  # nói đã mua
        "vừa mua laptop 25 triệu",
        "lương về 30 triệu",  # không phải khoản chi
        "đóng học phí 30 triệu",  # không có động từ mua
    ],
)
def test_other_sentences_are_not_asked_back(text):
    assert parse_chat(text, now=NOW).intent != AMBIGUOUS_PURCHASE_INTENT


def test_question_form_still_goes_to_purchase_check():
    assert parse_chat("mua laptop 25 triệu được không", now=NOW).intent == "evaluate_purchase"


def test_admin_can_set_the_amount_threshold_in_dong():
    import copy

    from app.services import config_documents, intent_cues

    doc = copy.deepcopy(intent_cues.default_data())
    doc["thresholds"]["ambiguous_purchase_min"]["value"] = 50_000_000  # ngưỡng số tiền, không bị kẹt trong khoảng 0..1 của độ chắc
    config_documents._validate_intent_cues(doc)
    doc["thresholds"]["uncertain_below"]["value"] = 50  # độ chắc vẫn phải 0..1
    with pytest.raises(Exception):
        config_documents._validate_intent_cues(doc)
