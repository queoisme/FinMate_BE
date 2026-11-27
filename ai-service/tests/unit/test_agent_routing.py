"""Chat: câu KẾ HOẠCH / DỰ BÁO được gắn ý định ask_agent để app hỏi AI Financial Agent, không bị đọc nhầm thành khoản chi hay đánh giá mua.

Từ khóa nằm ở app/data/intent_cues.json (mẫu agent_planning + danh sách agent_planning_phrases), admin sửa được; ở đây chỉ ghim hành vi.
Các câu "phải đi agent" lấy từ lần thử thật: bộ phân loại + luật cũ đọc "Chia ngân sách..., tiết kiệm 3 triệu" thành khoản chi 3 triệu.
"""

from datetime import UTC, datetime

import pytest

from app.services.transaction_parser import AGENT_INTENT, parse_chat

NOW = datetime(2026, 11, 17, 10, 0, tzinfo=UTC)


@pytest.mark.parametrize(
    "text",
    [
        "Mua xe 600 triệu sau 2 năm được không?",
        "Nếu thu nhập giảm 30% trong 3 tháng thì sao?",
        "Tôi sống được mấy tháng nếu mất việc?",
        "Chia ngân sách tháng này, tiết kiệm 3 triệu",
        "chia ngân sách giúp tôi, tiết kiệm 5 triệu",  # đã bị đọc thành mục tiêu "Chia ngân sách giúp" 5 triệu
        "chia ngan sach giup toi tiet kiem 5 trieu",
        "Bao lâu thì tôi đủ tiền mua nhà 2 tỷ?",
        "Lạm phát năm nay khoảng bao nhiêu?",
        "nếu thu nhap giam 20% thi sao",  # không dấu
        "5 năm nữa tôi có đủ mua nhà không",
        "dự báo số dư cuối tháng",
        "cuối tháng tôi còn dư bao nhiêu",
        "có nên vay mua xe không",
        "nên mua đất hay mua vàng",
        # dữ liệu quản lý: agent có công cụ đọc ngân sách / mục tiêu / khoản định kỳ ("đặt hạn mức ..." là lệnh tạo ngân sách, xem test_item_parser)
        "ngân sách ăn uống còn bao nhiêu",
        "mục tiêu của tôi đến đâu rồi",
        "sắp đến hạn khoản nào",
        "các khoản định kỳ của tôi",
    ],
)
def test_planning_questions_go_to_the_agent(text):
    parsed = parse_chat(text, None, now=NOW)

    assert parsed.intent == AGENT_INTENT
    assert parsed.entry is None and parsed.entries == () and parsed.query is None and parsed.purchase is None
    assert parsed.reply_text is None  # chat không trả lời: app hỏi agent


@pytest.mark.parametrize(
    ("text", "intent"),
    [
        ("ăn phở 45k", "add_expense"),
        ("mua cà phê 45k", "add_expense"),
        ("tháng này tôi tiêu bao nhiêu?", "ask_spending"),
        ("tôi tiêu bao nhiêu trong 3 tháng qua", "ask_spending"),  # "trong N tháng" cố ý KHÔNG phải dấu hiệu kế hoạch
        ("chi tiêu bao nhiêu tháng này", "ask_spending"),  # "bao nhiêu tháng" cũng không
        ("mua iphone 15tr được không", "evaluate_purchase"),  # kiểm tra mua thông thường vẫn do chat
        ("xin chào", "greet"),
        ("còn dư bao nhiêu tiền tháng này", "ask_spending"),  # "dự báo" gấp dấu thành "du bao" trùng "dư bao nhiêu"
    ],
)
def test_ordinary_sentences_are_not_sent_to_the_agent(text, intent):
    assert parse_chat(text, None, now=NOW).intent == intent


def test_the_agent_cue_comes_from_the_vocabulary_so_admin_edits_apply():
    from app.services import intent_cues

    data = intent_cues.default_data()
    data["lists"]["agent_planning_phrases"] = ["hoàn toàn mới"]
    intent_cues.apply(intent_cues.build(data, "test"))
    try:
        assert parse_chat("hoàn toàn mới", None, now=NOW).intent == AGENT_INTENT
        assert parse_chat("nếu thu nhập giảm 30% thì sao", None, now=NOW).intent != AGENT_INTENT
    finally:
        intent_cues.apply(intent_cues.CUES)


@pytest.mark.parametrize(
    "text",
    ["giải thích giúp tôi con số này", "Giải thích cho mình các số này với", "giải thích số", "giải thích con số vừa rồi", "giai thich gium toi so nay"],
)
def test_explain_requests_are_read_as_explain_numbers_even_with_polite_filler(text):
    assert parse_chat(text, now=NOW).intent == "explain_numbers"


def test_explain_phrase_does_not_swallow_other_questions():
    assert parse_chat("giải thích giúp tôi cách tiết kiệm", now=NOW).intent != "explain_numbers"
