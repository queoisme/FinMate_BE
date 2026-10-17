"""Chat và giọng nói: sản phẩm do AI phân loại (như hóa đơn), luật chỉ là dự phòng.

AI quyết trước khi người dùng đã đồng ý AI; thuốc không bao giờ gửi ra ngoài; thu nhập, chuyển tiền và chi cho người khác không phải "sản phẩm" nên không gửi;
AI lỗi hay chậm thì giữ kết quả của luật. Chỉ gửi cụm sản phẩm (đã bỏ số tiền), gộp mọi khoản của một câu vào một lời gọi.
"""

import asyncio
import json
from datetime import UTC, datetime

import httpx
import pytest

from app.api.v1.bill import get_ai_instruction, get_bill_knowledge, get_chat_gemini_client, get_classify_stats, get_gemini_client
from app.api.v1.chat import _with_cached_categories
from app.core.config import settings
from app.main import app
from app.services.chat_ai import apply_ai_categories
from app.bill.gemini import GeminiClient, GeminiFailed
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
    app.dependency_overrides[get_chat_gemini_client] = lambda: fake
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
        for dep in (get_bill_knowledge, get_chat_gemini_client, get_ai_instruction, get_classify_stats):
            app.dependency_overrides.pop(dep, None)
    body = res.json()
    assert res.status_code == 200
    assert bool(fake.calls) is allow_ai
    assert body["entry"]["category_slug"] == ("shopping" if allow_ai else None)
    if allow_ai:
        assert "60k" not in " ".join(fake.calls[0])


async def test_the_chat_route_uses_the_chat_client_and_never_the_receipt_one():
    chat_fake, bill_fake = FakeGemini(lambda n: "shopping"), FakeGemini(lambda n: "food")
    app.dependency_overrides[get_bill_knowledge] = lambda: FakeKnowledge()
    app.dependency_overrides[get_chat_gemini_client] = lambda: chat_fake
    app.dependency_overrides[get_gemini_client] = lambda: bill_fake
    app.dependency_overrides[get_ai_instruction] = lambda: "INSTR"

    class Recorder:
        async def record(self, counts):
            return None

    app.dependency_overrides[get_classify_stats] = lambda: Recorder()
    try:
        transport = httpx.ASGITransport(app=app)
        async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
            res = await client.post("/api/v1/chat/parse", headers={"Authorization": f"Bearer {settings.internal_api_key}"},
                                    json={"text": "mua ổ cắm điện 60k", "user_id_hash": "a" * 64, "allow_ai": True})
    finally:
        for dep in (get_bill_knowledge, get_chat_gemini_client, get_gemini_client, get_ai_instruction, get_classify_stats):
            app.dependency_overrides.pop(dep, None)
    assert res.status_code == 200 and res.json()["entry"]["category_slug"] == "shopping"
    assert chat_fake.calls and bill_fake.calls == []  # hai luồng không chung model, không chung cầu dao


def test_the_chat_client_is_built_for_speed_and_its_budgets_nest():
    client = get_chat_gemini_client()
    models = client._models
    assert models[0] == settings.chat_gemini_model == "gemini-3.1-flash-lite"  # model nhanh đứng đầu
    assert "gemini-3-flash-preview" in models[1:]  # model chính để dự phòng
    assert client._backup_key is None  # chat không đi OpenRouter: model miễn phí chậm và không có chỗ trong 9 giây
    # Mỗi model có trần riêng < trần cả chuỗi < trần ngoài cùng: chuỗi tự thất bại (mở cầu dao 30 giây) TRƯỚC khi bị hủy từ ngoài,
    # nếu không thì lúc Gemini chậm mọi tin nhắn đều phải chờ đủ trần ngoài.
    assert client._timeout < client._overall_timeout < settings.chat_ai_timeout_seconds


async def test_the_default_time_limit_comes_from_settings(monkeypatch):
    class Slow(FakeGemini):
        async def classify(self, names, instruction):
            await asyncio.sleep(1)
            return ["shopping"] * len(names)

    monkeypatch.setattr(settings, "chat_ai_timeout_seconds", 0.05)
    parsed = parse_chat("ăn phở 45k", None, now=NOW)
    out = await apply_ai_categories(parsed, FakeKnowledge(), Slow(), "LUAT", True)  # không truyền timeout: lấy từ settings
    assert out.entries[0].category == "food"


async def test_a_slow_chat_chain_fails_by_itself_and_opens_the_breaker():
    async def slow(request):
        await asyncio.sleep(1)
        return httpx.Response(200, json={})

    client = GeminiClient("key", "m1", 3.5, transport=httpx.MockTransport(slow), overall_timeout=0.05, fallback_models=("m2",))
    with pytest.raises(GeminiFailed):
        await client.classify(["ổ cắm điện"], "LUAT")
    assert client.available is False  # cầu dao mở: tin nhắn kế dùng luật ngay, không ai phải chờ lại


async def test_a_chat_model_that_fails_hands_over_to_the_next_one():
    answer = {"candidates": [{"content": {"parts": [{"text": json.dumps([{"index": 0, "category": "shopping"}])}]}}]}

    def handler(request):
        return httpx.Response(503) if "/m1:" in str(request.url) else httpx.Response(200, json=answer)

    client = GeminiClient("key", "m1", 3.5, transport=httpx.MockTransport(handler), fallback_models=("m2",))
    assert await client.classify(["ổ cắm điện"], "LUAT") == ["shopping"]

