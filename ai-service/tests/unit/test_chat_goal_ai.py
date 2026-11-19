"""Câu đặt mục tiêu mà luật không đọc ra hạn ("đến năm 2029", "sau chừng 3 năm"): Gemini chỉ CỤM THỜI HẠN, ngày do code tính.

An toàn được ghim ở đây: chỉ khi đã đồng ý AI; AI chỉ thấy câu đã che mọi chữ số; con số thật lấy từ câu gốc theo ký hiệu (không từ AI), hạn tính bằng hàm của luật và phải ở tương lai;
số tiền không đổi; tên AI chỉ nhận khi mọi chữ của nó có trong câu; kết quả đánh dấu ai_assisted (app không tự tạo); AI lỗi hay sai quy ước thì giữ kết quả của luật.
"""

import json
from datetime import datetime

from app.bill.gemini import GeminiFailed
from app.services.chat_ai import apply_ai_goal, needs_goal_detection, needs_goal_help
from app.services.transaction_parser import VN, parse_chat

NOW = datetime(2026, 11, 19, 16, 42, tzinfo=VN)


class FakeLlm:
    """``reply``: trả lời của bước ĐỌC HẠN; ``is_goal``: trả lời của bước NHẬN RA mục tiêu (phân biệt theo schema)."""

    def __init__(self, reply=None, available=True, fail=False, raw=None, is_goal=False):
        self.reply, self.available, self.fail, self.raw, self.is_goal = reply or {}, available, fail, raw, is_goal
        self.prompts: list[str] = []

    async def generate_text(self, prompt, schema):
        self.prompts.append(prompt)
        if self.fail:
            raise GeminiFailed("boom")
        if self.raw is not None:
            return self.raw
        if "is_goal" in schema["properties"]:
            return json.dumps({"is_goal": self.is_goal})
        return json.dumps({"duration_slot": 0, "duration_unit": "none", "year_slot": 0, "month_slot": 0, "name": "", **self.reply}, ensure_ascii=False)


def rules(text):
    return parse_chat(text, None, now=NOW)


async def reparse(text):
    return parse_chat(text, None, now=NOW)


async def run(text, llm, allow_ai=True, stats=None):
    return await apply_ai_goal(text, rules(text), reparse, llm, allow_ai, NOW, stats=stats)


YEAR_SENTENCE = "đặt mục tiêu mua nhà 1 tỷ vào năm dự kiến là 2029"  # luật chỉ biết "năm 2029" đứng liền, không biết "năm dự kiến là 2029"
SPAN_SENTENCE = "đặt mục tiêu mua xe 500 triệu, dự kiến mua sau chừng 3 năm"


def test_the_rules_really_miss_the_deadline_of_these_sentences():
    for text in (YEAR_SENTENCE, SPAN_SENTENCE):
        goal = rules(text).goal
        assert goal is not None and goal.deadline is None, text
        assert needs_goal_help(text, rules(text))


def test_no_help_when_the_rules_found_the_deadline_or_the_sentence_has_no_time_words():
    assert not needs_goal_help("đặt mục tiêu mua nhà 1 tỷ trong 3 năm", rules("đặt mục tiêu mua nhà 1 tỷ trong 3 năm"))
    assert not needs_goal_help("đặt mục tiêu mua laptop 20 triệu", rules("đặt mục tiêu mua laptop 20 triệu"))
    assert not needs_goal_help("mua cà phê 45k", rules("mua cà phê 45k"))


async def test_a_specific_year_becomes_the_end_of_that_year_computed_by_code():
    llm = FakeLlm({"duration_unit": "none", "year_slot": 2, "name": "Mua nhà"})
    stats: dict[str, int] = {}

    parsed = await run(YEAR_SENTENCE, llm, stats=stats)

    goal = parsed.goal
    assert (goal.deadline, goal.target, goal.ai_assisted) == (datetime(2029, 12, 31, 23, 59, 59, tzinfo=VN), 1_000_000_000, True)
    assert goal.name == "Mua nhà" and stats == {"goal": 1}
    sentence = llm.prompts[0].rsplit("CÂU:", 1)[1]
    assert "{2}" in sentence and "2029" not in sentence and "{1} tỷ" in sentence, "AI chỉ thấy câu đã che số"


async def test_a_span_uses_the_digit_of_the_original_sentence_not_a_number_from_the_ai():
    parsed = await run(SPAN_SENTENCE, FakeLlm({"duration_unit": "year", "duration_slot": 2}))

    assert parsed.goal.deadline == datetime(2029, 11, 19, 23, 59, 59, tzinfo=VN)
    assert parsed.goal.target == 500_000_000 and parsed.goal.ai_assisted


async def test_without_ai_consent_or_with_ai_unavailable_nothing_is_sent():
    llm = FakeLlm({"duration_unit": "year", "duration_slot": 2})

    assert (await run(SPAN_SENTENCE, llm, allow_ai=False)).goal.deadline is None
    assert (await run(SPAN_SENTENCE, FakeLlm(available=False))).goal.deadline is None
    assert llm.prompts == []


async def test_failures_and_wrong_conventions_keep_the_rules_result():
    for llm in (FakeLlm(fail=True), FakeLlm(raw="không phải JSON"), FakeLlm(raw='["x"]')):
        parsed = await run(SPAN_SENTENCE, llm)
        assert parsed.goal.deadline is None and not parsed.goal.ai_assisted


async def test_an_implausible_or_past_deadline_is_refused():
    cases = [
        {"duration_unit": "year", "duration_slot": 1},  # {1} là số TIỀN 500: 500 năm không hợp lý
        {"duration_unit": "year", "duration_slot": 9},  # ký hiệu không tồn tại
        {"duration_unit": "none", "year_slot": 2},  # {2} là 3, không phải một năm
        {"duration_unit": "month", "duration_slot": 0},
    ]
    for reply in cases:
        assert (await run(SPAN_SENTENCE, FakeLlm(reply))).goal.deadline is None, reply

    past = await run("đặt mục tiêu mua nhà 1 tỷ vào năm dự kiến là 2020", FakeLlm({"year_slot": 2}))
    assert past.goal.deadline is None


async def test_a_money_or_date_slot_with_separators_is_not_a_duration():
    # "1.000" (tiền) và "30/12" (ngày) không phải một con số chỉ thời gian
    parsed = await run("đặt mục tiêu mua nhà 1.000 triệu sau chừng 3 năm", FakeLlm({"duration_unit": "year", "duration_slot": 1}))

    assert parsed.goal.deadline is None


async def test_the_ai_name_replaces_the_rules_name_only_when_it_is_a_default_or_polluted_and_only_with_words_from_the_sentence():
    polluted = await run(YEAR_SENTENCE, FakeLlm({"year_slot": 2, "name": "Mua nhà"}))
    assert polluted.goal.name == "Mua nhà" and not polluted.goal.name_is_default

    invented = await run(YEAR_SENTENCE, FakeLlm({"year_slot": 2, "name": "Biệt thự Đà Lạt"}))
    assert invented.goal.deadline is not None and invented.goal.name == rules(YEAR_SENTENCE).goal.name  # chữ bịa: giữ tên của luật

    with_time = await run(YEAR_SENTENCE, FakeLlm({"year_slot": 2, "name": "Mua nhà năm 2029"}))
    assert with_time.goal.name == rules(YEAR_SENTENCE).goal.name  # tên dính số/thời hạn: không nhận


async def test_month_and_year_slots_become_the_end_of_that_month():
    parsed = await run("đặt mục tiêu mua nhà 1 tỷ, chốt tháng 6 của năm dự kiến là 2028", FakeLlm({"month_slot": 2, "year_slot": 3}))

    assert parsed.goal.deadline == datetime(2028, 6, 30, 23, 59, 59, tzinfo=VN) and parsed.goal.ai_assisted


DETECT_SENTENCES = ["mình muốn tích lũy 500 triệu mua nhà trong 3 năm", "em muốn có 2 tỷ để mua nhà trong 7 năm", "mình cần góp 50 triệu đi du lịch trong 6 tháng"]


def test_the_rules_read_these_goal_sentences_as_something_else_and_the_ai_step_is_asked():
    for text in DETECT_SENTENCES:
        parsed = rules(text)
        assert parsed.goal is None and needs_goal_detection(text, parsed), (text, parsed.intent)


def test_no_detection_for_questions_transfers_plain_expenses_or_sentences_without_an_amount():
    for text in ("tôi muốn tiết kiệm bớt tiền ăn", "chuyển 2 triệu sang tiết kiệm", "ăn phở 45k", "mình có nên tích lũy 500 triệu không?", "mua laptop 20 triệu"):
        assert not needs_goal_detection(text, rules(text)), text


async def test_the_ai_recognises_the_goal_and_the_rules_then_read_amount_deadline_and_name():
    stats: dict[str, int] = {}

    parsed = await run(DETECT_SENTENCES[0], FakeLlm(is_goal=True), stats=stats)

    goal = parsed.goal
    assert parsed.intent == "create_goal" and goal.ai_assisted and stats == {"goal_detected": 1}
    assert goal.target == 500_000_000 and goal.deadline == datetime(2029, 11, 19, 23, 59, 59, tzinfo=VN), "tiền và hạn do luật đọc, không phải AI"


async def test_when_the_ai_says_it_is_not_a_goal_nothing_changes():
    text = DETECT_SENTENCES[0]

    parsed = await run(text, FakeLlm(is_goal=False))

    assert parsed.goal is None and parsed.intent == rules(text).intent


async def test_detection_failure_keeps_the_rules_result():
    text = DETECT_SENTENCES[0]

    for llm in (FakeLlm(fail=True), FakeLlm(raw="không phải JSON")):
        parsed = await run(text, llm)
        assert parsed.goal is None and parsed.intent == rules(text).intent


async def test_detection_is_not_attempted_without_consent():
    llm = FakeLlm(is_goal=True)

    assert (await run(DETECT_SENTENCES[0], llm, allow_ai=False)).goal is None
    assert llm.prompts == []


async def test_the_amount_is_never_changed_by_the_ai():
    parsed = await run(YEAR_SENTENCE, FakeLlm({"year_slot": 2, "name": "1 tỷ"}))

    assert parsed.goal.target == 1_000_000_000

