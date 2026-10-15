"""Chat và giọng nói: sản phẩm do AI phân loại (như hóa đơn), luật chỉ là dự phòng.

AI quyết trước khi người dùng đã đồng ý AI; thuốc không bao giờ gửi ra ngoài; thu nhập, chuyển tiền và chi cho người khác không phải "sản phẩm" nên không gửi;
AI lỗi hay chậm thì giữ kết quả của luật. Chỉ gửi cụm sản phẩm (đã bỏ số tiền), gộp mọi khoản của một câu vào một lời gọi.
"""

import asyncio
from datetime import UTC, datetime

import httpx
import pytest

from app.api.v1.bill import get_ai_instruction, get_bill_knowledge, get_classify_stats, get_gemini_client
from app.api.v1.chat import _with_cached_categories
from app.core.config import settings
from app.main import app
from app.services.chat_ai import apply_ai_categories
from app.services.transaction_parser import parse_chat
from tests.unit.test_item_classifier import FakeGemini, FakeKnowledge

NOW = datetime(2026, 10, 15, 10, 0, tzinfo=UTC)


async def run(text, gemini, allow_ai=True, knowledge=None, timeout=6.0):
    parsed = parse_chat(text, None, now=NOW)
    out = await apply_ai_categories(parsed, knowledge or FakeKnowledge(), gemini, "LUAT", allow_ai, timeout=timeout)
    return [(e.amount, e.category, e.category_source, e.auto_save_ok) for e in out.entries]


async def test_without_ai_consent_nothing_is_sent_and_the_rules_decide():
    gemini = FakeGemini(lambda n: "shopping")
    got = await run("mua cây lau nhà 80k", gemini, allow_ai=False)
    assert gemini.calls == []
    assert got[0][0] == 80_000


async def test_ai_decides_where_it_disagrees_with_the_rules_and_the_result_needs_confirmation():
    # luật nói Ăn uống, AI nói Giải trí: AI quyết, và vì khác luật nên không tự lưu
    ((amount, category, source, auto),) = await run("ăn phở 45k", FakeGemini(lambda n: "entertainment"))
    assert (amount, category, source, auto) == (45_000, "entertainment", "ai", False)


async def test_ai_fills_in_a_product_the_rules_do_not_know():
    ((_, category, source, auto),) = await run("mua ổ cắm điện 60k", FakeGemini(lambda n: "shopping"))
    assert (category, source, auto) == ("shopping", "ai", False)


async def test_when_ai_and_rules_agree_the_quick_save_is_kept():
    ((_, category, _, auto),) = await run("ăn phở 45k", FakeGemini(lambda n: "food"))
    assert (category, auto) == ("food", True)


async def test_only_the_product_phrase_is_sent_in_one_call_for_the_whole_sentence():
    gemini = FakeGemini(lambda n: "food")
    await run("mua thịt lợn 30k và ăn bánh tráng trộn 20k", gemini)
    assert len(gemini.calls) == 1
    sent = " ".join(gemini.calls[0])
    assert "30k" not in sent and "20k" not in sent and "thịt lợn" in sent


async def test_medicine_is_never_sent_to_ai():
    gemini = FakeGemini(lambda n: "shopping")
    got = await run("mua vỉ thuốc hạ sốt 20k", gemini)
    assert gemini.calls == []
    assert got[0][1] == "health"  # luật quyết


async def test_income_transfers_and_money_for_other_people_are_not_products():
    gemini = FakeGemini(lambda n: "shopping")
    await run("nhận lương 15 triệu", gemini)
    await run("chuyển khoản cho mẹ 500k", gemini)
    await run("trả nợ cho Nam 300k", gemini)
    assert gemini.calls == []


async def test_ai_failure_keeps_the_rule_result():
    ((_, category, source, auto),) = await run("ăn phở 45k", FakeGemini(fail=True))
    assert (category, source, auto) == ("food", "rule", True)


async def test_a_slow_ai_is_given_up_on_so_the_chat_is_not_held_up():
    class Slow(FakeGemini):
        async def classify(self, names, instruction):
            await asyncio.sleep(1)
            return ["shopping"] * len(names)

    ((_, category, _, _),) = await run("ăn phở 45k", Slow(), timeout=0.05)
    assert category == "food"


async def test_a_remembered_answer_is_used_without_calling_ai():
    knowledge = FakeKnowledge(cache={"mua o cam dien": "shopping"})
    gemini = FakeGemini(lambda n: "food")
    ((_, category, source, _),) = await run("mua ổ cắm điện 60k", gemini, knowledge=knowledge)
    assert (category, source) == ("shopping", "cache") and gemini.calls == []


@pytest.mark.parametrize("allow_ai", [True, False])
async def test_the_route_sends_allow_ai_and_never_the_amount(allow_ai):
    fake = FakeGemini(lambda n: "shopping")
    app.dependency_overrides[get_bill_knowledge] = lambda: FakeKnowledge()
    app.dependency_overrides[get_gemini_client] = lambda: fake
    app.dependency_overrides[get_ai_instruction] = lambda: "INSTR"

    class Recorder:
        async def record(self, counts):
            return None

    app.dependency_overrides[get_classify_stats] = lambda: Recorder()
    try:
        transport = httpx.ASGITransport(app=app)
        async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
            res = await client.post("/api/v1/chat/parse", headers={"Authorization": f"Bearer {settings.internal_api_key}"},
                                    json={"text": "mua ổ cắm điện 60k", "user_id_hash": "a" * 64, "allow_ai": allow_ai})
    finally:
        for dep in (get_bill_knowledge, get_gemini_client, get_ai_instruction, get_classify_stats):
            app.dependency_overrides.pop(dep, None)
    body = res.json()
    assert res.status_code == 200
    assert bool(fake.calls) is allow_ai
    assert body["entry"]["category_slug"] == ("shopping" if allow_ai else None)
    if allow_ai:
        assert "60k" not in " ".join(fake.calls[0])
