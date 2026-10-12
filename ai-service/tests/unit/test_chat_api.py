"""``POST /api/v1/chat/parse``: xác thực, quy đổi sang slug backend, và Rasa tắt thì vẫn chạy bằng luật."""

import httpx
import pytest

from app.api.v1 import chat as chat_route
from app.chat.nlu import RasaNlu
from app.core.config import settings
from app.core.enums import CATEGORY_SLUGS
from app.main import app

HASH = "a" * 64


@pytest.fixture(autouse=True)
def no_rasa():
    chat_route.set_nlu(None)
    yield
    chat_route.set_nlu(None)


async def post(text, headers=True, **extra):
    transport = httpx.ASGITransport(app=app)  # không chạy lifespan nên không cần Postgres
    auth = {"Authorization": f"Bearer {settings.internal_api_key}"} if headers else {}
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        return await client.post("/api/v1/chat/parse", json={"text": text, "user_id_hash": HASH, **extra}, headers=auth)


async def test_missing_key_is_rejected():
    assert (await post("đi ăn 200k", headers=False)).status_code == 401


async def test_expense_becomes_a_backend_slug():
    body = (await post("đi ăn 200k")).json()
    assert body["intent"] == "add_expense" and body["nlu"]["source"] == "rules"
    entry = body["entry"]
    assert (entry["kind"], entry["amount_cents"], entry["category_slug"], entry["auto_save_ok"]) == ("expense", 200_000, "food", True)
    assert entry["category_slug"] in CATEGORY_SLUGS


@pytest.mark.parametrize(
    ("text", "slug"),
    [("đóng tiền wifi 250k", "bills"), ("khám bệnh 300k", "health"), ("đi grab 50k", "transport"), ("đi chợ 150k", "food"), ("tiền nhà 3 củ 8", "housing")],
)
async def test_every_category_is_a_valid_backend_slug(text, slug):
    assert (await post(text)).json()["entry"]["category_slug"] == slug


async def test_income_is_the_single_income_slug_with_its_type():
    entry = (await post("nhận lương 14 củ 5")).json()["entry"]
    assert (entry["kind"], entry["category_slug"], entry["income_type"], entry["auto_save_ok"]) == ("income", "income", "salary", False)


async def test_transfer_has_no_category():
    entry = (await post("chuyển 2 triệu sang tiết kiệm")).json()["entry"]
    assert entry["kind"] == "transfer" and entry["category_slug"] is None


async def test_unknown_category_is_null_not_other():
    entry = (await post("250k")).json()["entry"]
    assert entry["category_slug"] is None and entry["auto_save_ok"] is False


async def test_the_dictionary_fills_a_merchant_the_chat_vocabulary_does_not_know():
    entry = (await post("ghé highlands 55k")).json()["entry"]
    assert entry["category_slug"] == "food" and entry["auto_save_ok"] is True


async def test_total_question_returns_a_query_not_numbers():
    body = (await post("tháng này tôi tiêu bao nhiêu")).json()
    assert body["intent"] == "ask_spending" and body["entry"] is None
    assert (body["query"]["metric"], body["query"]["period_key"]) == ("expense", "thang nay")
    assert body["query"]["start"].endswith("+07:00")


async def test_text_is_capped_and_hash_is_required():
    assert (await post("a" * 501)).status_code == 422
    assert (await post("đi ăn 200k", user_id_hash="short")).status_code == 422


async def test_rasa_is_used_when_available_and_ignored_when_down():
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, json={"intent": {"name": "add_expense", "confidence": 0.97}, "entities": []})

    chat_route.set_nlu(RasaNlu("http://rasa:5005", client=httpx.AsyncClient(transport=httpx.MockTransport(handler))))
    body = (await post("đi ăn 200k")).json()
    assert body["nlu"] == {"source": "rasa", "intent": "add_expense", "confidence": 0.97}

    def down(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("down")

    chat_route.set_nlu(RasaNlu("http://rasa:5005", client=httpx.AsyncClient(transport=httpx.MockTransport(down))))
    body = (await post("đi ăn 200k")).json()
    assert body["nlu"]["source"] == "rules" and body["entry"]["category_slug"] == "food"


async def test_rasa_circuit_breaker_stops_calling_after_a_failure():
    calls = []

    def down(request: httpx.Request) -> httpx.Response:
        calls.append(1)
        raise httpx.ConnectError("down")

    nlu = RasaNlu("http://rasa:5005", client=httpx.AsyncClient(transport=httpx.MockTransport(down)))
    assert await nlu.parse("a") is None and await nlu.parse("b") is None
    assert len(calls) == 1


async def test_response_carries_entries_and_the_date():
    body = (await post("hôm qua ăn phở 45k, cà phê 30k")).json()
    assert [e["amount_cents"] for e in body["entries"]] == [45_000, 30_000]
    assert body["entry"] == body["entries"][0] and body["entries"][0]["occurred_at"] is not None
    assert all(not e["auto_save_ok"] for e in body["entries"])


async def test_single_entry_is_also_listed_and_has_no_date():
    body = (await post("đi ăn 200k")).json()
    assert body["entries"] == [body["entry"]] and body["entry"]["occurred_at"] is None
