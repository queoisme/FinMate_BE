"""Chat: luật không hiểu câu gõ sai/viết tắt thì Gemini SỬA CHÍNH TẢ, rồi chính luật đọc lại.

An toàn được ghim ở đây: chỉ khi đã đồng ý AI; AI chỉ thấy câu đã che mọi chữ số; AI chỉ trả câu đã sửa (không nhãn, không số tiền); AI trả sai quy ước hay lỗi thì giữ
kết quả của luật; kết quả nhờ AI không bao giờ tự lưu.
"""

import asyncio
import json
from datetime import UTC, datetime

import httpx
import pytest

from app.api.v1.bill import get_ai_instruction, get_bill_knowledge, get_chat_gemini_client, get_classify_stats
from app.bill.gemini import GeminiFailed
from app.core.config import settings
from app.main import app
from app.services.chat_ai import _mask, _unmask, apply_ai_rewrite, needs_rewrite
from app.services.transaction_parser import parse_chat
from tests.unit.test_item_classifier import FakeKnowledge

NOW = datetime(2026, 10, 15, 10, 0, tzinfo=UTC)

# Luật không hiểu ("dc k" chưa biết) nên ngoài phạm vi; và luật hiểu NHẦM thành một khoản chi
OUT_OF_SCOPE = "t co nen mua iphone 15tr dc k"
MISREAD_AS_EXPENSE = "mik mua dt moi 15tr dc ko"


class FakeLlm:
    """``answer``: hàm câu đã che số -> câu trả lời (chữ) của Gemini; ``fail``: ném GeminiFailed; ``raw``: trả nguyên chuỗi này."""

    def __init__(self, answer=lambda masked: masked, available=True, fail=False, raw=None):
        self.answer, self.available, self.fail, self.raw = answer, available, fail, raw
        self.prompts: list[str] = []

    async def generate_text(self, prompt, schema):
        self.prompts.append(prompt)
        if self.fail:
            raise GeminiFailed("boom")
        if self.raw is not None:
            return self.raw
        masked = json.loads(prompt.rsplit("CÂU: ", 1)[1])
        return json.dumps({"text": self.answer(masked)}, ensure_ascii=False)


# Bộ phân loại thật nói "ngoài phạm vi" cho các câu này (đã đo trong container); luật đơn thuần thì đọc thành khoản chi
CLASSIFIER_SAYS_OUT_OF_SCOPE = {OUT_OF_SCOPE, "xyz abc 45k"}
OOS_NLU = {"intent": "out_of_scope", "confidence": 0.99, "entities": {}}


def parse(text):
    return parse_chat(text, OOS_NLU if text in CLASSIFIER_SAYS_OUT_OF_SCOPE else None, now=NOW)


async def reparse(text):
    return parse_chat(text, None, now=NOW)


async def run(text, llm, allow_ai=True, **kw):
    return await apply_ai_rewrite(text, parse(text), reparse, llm, allow_ai, **kw)


def test_the_rules_really_do_misread_these_sentences():
    assert parse(OUT_OF_SCOPE).intent == "out_of_scope"
    assert parse(MISREAD_AS_EXPENSE).intent == "add_expense"
    assert needs_rewrite(OUT_OF_SCOPE, parse(OUT_OF_SCOPE)) and needs_rewrite(MISREAD_AS_EXPENSE, parse(MISREAD_AS_EXPENSE))


async def test_without_ai_consent_nothing_is_sent():
    llm = FakeLlm()
    assert (await run(OUT_OF_SCOPE, llm, allow_ai=False)).intent == "out_of_scope"
    assert llm.prompts == []


async def test_a_sentence_the_rules_do_not_understand_is_fixed_and_read_again_by_the_rules():
    llm = FakeLlm(lambda masked: "tôi có nên mua iphone {1}tr được không")  # "15tr" bị che thành "{1}tr": đơn vị ở lại trong câu
    parsed = await run(OUT_OF_SCOPE, llm)
    assert parsed.intent == "evaluate_purchase"
    assert parsed.purchase.amount_cents == 15_000_000  # số tiền do luật đọc từ câu gốc, không do AI
    assert parsed.purchase.item == "iphone"


async def test_a_question_the_rules_misread_as_an_expense_is_corrected():
    llm = FakeLlm(lambda masked: "mình mua điện thoại mới {1}tr được không")
    parsed = await run(MISREAD_AS_EXPENSE, llm)
    assert parsed.intent == "evaluate_purchase" and parsed.purchase.amount_cents == 15_000_000


async def test_the_ai_never_sees_the_amount():
    llm = FakeLlm(lambda masked: "tôi có nên mua iphone {1} được không")
    await run(OUT_OF_SCOPE, llm)
    sent = llm.prompts[0].rsplit("CÂU: ", 1)[1]
    assert "15" not in sent and "{1}" in sent


@pytest.mark.parametrize(
    "answer",
    [
        "tôi có nên mua iphone được không",  # mất ký hiệu số tiền
        "tôi có nên mua iphone {1} {1} được không",  # thừa ký hiệu
        "tôi có nên mua iphone {2} được không",  # sai ký hiệu
        "tôi có nên mua iphone 20tr được không",  # AI tự bịa số tiền
        "tôi có nên mua iphone {1} 5 lần được không",  # thêm chữ số ngoài ký hiệu
    ],
)
async def test_an_answer_that_breaks_the_placeholder_contract_is_ignored(answer):
    parsed = await run(OUT_OF_SCOPE, FakeLlm(lambda masked: answer))
    assert parsed.intent == "out_of_scope"


@pytest.mark.parametrize("raw", ["không phải JSON", json.dumps(["x"]), json.dumps({"text": ""}), json.dumps({"text": 5}), json.dumps({"text": "a" * 500})])
async def test_a_malformed_answer_keeps_the_rule_result(raw):
    assert (await run(OUT_OF_SCOPE, FakeLlm(raw=raw))).intent == "out_of_scope"


async def test_ai_failure_or_unavailable_keeps_the_rule_result():
    assert (await run(OUT_OF_SCOPE, FakeLlm(fail=True))).intent == "out_of_scope"
    unavailable = FakeLlm(available=False)
    assert (await run(OUT_OF_SCOPE, unavailable)).intent == "out_of_scope"
    assert unavailable.prompts == []


async def test_a_slow_ai_is_given_up_on():
    class Slow(FakeLlm):
        async def generate_text(self, prompt, schema):
            await asyncio.sleep(1)
            return await super().generate_text(prompt, schema)

    assert (await run(OUT_OF_SCOPE, Slow(), timeout=0.05)).intent == "out_of_scope"


async def test_no_change_from_the_ai_means_no_second_read():
    calls = []

    async def counting(text):
        calls.append(text)
        return await reparse(text)

    parsed = await apply_ai_rewrite(OUT_OF_SCOPE, parse(OUT_OF_SCOPE), counting, FakeLlm(), True)
    assert parsed.intent == "out_of_scope" and calls == []


async def test_a_fixed_sentence_that_the_rules_still_do_not_understand_changes_nothing():
    parsed = await run(OUT_OF_SCOPE, FakeLlm(lambda masked: "tôi muốn hỏi iphone {1}"))
    assert parsed.intent == "out_of_scope"


async def test_a_record_read_from_a_fixed_sentence_is_never_saved_without_confirmation():
    original = parse("xyz abc 45k")
    assert original.intent == "out_of_scope"
    good = parse_chat("ăn phở 45k", None, now=NOW)
    assert good.entries and good.entries[0].auto_save_ok  # chính luật tự lưu được câu này

    async def stub(_text):
        return good

    parsed = await apply_ai_rewrite("xyz abc 45k", original, stub, FakeLlm(lambda masked: "ăn phở {1}"), True)
    assert parsed.intent == "add_expense"
    assert parsed.entries and all(not e.auto_save_ok for e in parsed.entries) and not parsed.entry.auto_save_ok


@pytest.mark.parametrize("text", ["ăn phở 45k", "mua cà phê 45k", "xin chào", "thời tiết hôm nay thế nào", "tháng này tôi tiêu bao nhiêu"])
async def test_sentences_the_rules_understand_or_that_are_not_about_money_never_reach_the_ai(text):
    llm = FakeLlm()
    parsed = parse_chat(text, None, now=NOW)
    assert not needs_rewrite(text, parsed)
    await apply_ai_rewrite(text, parsed, reparse, llm, True)
    assert llm.prompts == []


def test_masking_hides_every_run_of_digits_and_restores_them_exactly():
    masked, slots = _mask("mua iphone15 giá 1.500.000đ ngày 16/11 lúc 9:30")
    assert masked == "mua iphone{1} giá {2}đ ngày {3} lúc {4}"
    assert _unmask(masked, slots) == "mua iphone15 giá 1.500.000đ ngày 16/11 lúc 9:30"


async def test_the_route_fixes_the_sentence_with_the_chat_client_when_the_user_agreed():
    llm = FakeLlm(lambda masked: "tôi có nên mua iphone {1}tr được không")

    class Recorder:
        counts: dict = {}

        async def record(self, counts):
            Recorder.counts = counts

    app.dependency_overrides[get_bill_knowledge] = lambda: FakeKnowledge()
    app.dependency_overrides[get_chat_gemini_client] = lambda: llm
    app.dependency_overrides[get_ai_instruction] = lambda: "INSTR"
    app.dependency_overrides[get_classify_stats] = lambda: Recorder()
    try:
        transport = httpx.ASGITransport(app=app)
        async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
            headers = {"Authorization": f"Bearer {settings.internal_api_key}"}
            body = {"text": OUT_OF_SCOPE, "user_id_hash": "a" * 64}
            agreed = await client.post("/api/v1/chat/parse", headers=headers, json={**body, "allow_ai": True})
            declined = await client.post("/api/v1/chat/parse", headers=headers, json={**body, "allow_ai": False})
    finally:
        for dep in (get_bill_knowledge, get_chat_gemini_client, get_ai_instruction, get_classify_stats):
            app.dependency_overrides.pop(dep, None)
    assert agreed.status_code == 200 and agreed.json()["intent"] == "evaluate_purchase"
    assert agreed.json()["purchase"]["amount_cents"] == 15_000_000
    assert declined.json()["intent"] != "evaluate_purchase"  # không đồng ý AI: giữ kết quả của luật/bộ phân loại
    assert len(llm.prompts) == 1  # chỉ lần đã đồng ý mới gọi AI
    assert Recorder.counts.get("chat_ai_rewrite") == 1
