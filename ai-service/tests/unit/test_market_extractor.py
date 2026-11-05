"""Rút thông tin từ bài báo: chỉ nhận đúng mẫu, không có key thì không đoán, cần khóa nội bộ, không log nội dung."""
import json

import httpx
import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

from app.api.v1 import market_data
from app.bill.gemini import GeminiClient
from app.core.config import settings
from app.services.market_extractor import MAX_EVIDENCE_CHARS, MAX_SIGNALS, MAX_TEXT_CHARS, parse_signals

SIGNAL = {
    "topic": "fuel",
    "direction": "up",
    "fact_status": "actual",
    "change_percent": 3.5,
    "effective_date": "2026-11-05",
    "evidence": "Giá xăng tăng 3,5% từ chiều nay.",
}
ARTICLE_SECRET = "NOI-DUNG-BAI-BAO-RIENG"
TOPIC_KEYS = ["fuel", "cooking_gas", "other"]
TOPICS = [{"key": "fuel", "description": "xăng dầu"}, {"key": "cooking_gas", "description": "gas nấu ăn"}]


def _gemini_reply(payload) -> httpx.Response:
    return httpx.Response(200, json={"candidates": [{"content": {"parts": [{"text": json.dumps(payload)}]}}]})


def _app(client: GeminiClient) -> TestClient:
    app = FastAPI()
    app.include_router(market_data.router, prefix="/api/v1")
    app.dependency_overrides[market_data.get_market_gemini_client] = lambda: client
    return TestClient(app)


def _client(handler, key: str | None = "khoa") -> GeminiClient:
    return GeminiClient(key, "model-a", 1.0, transport=httpx.MockTransport(handler), health_channel="market-test")


def _post(test_client: TestClient, **body):
    return test_client.post(
        "/api/v1/market-data/extract",
        json={"title": "Giá xăng", "text": f"Bài {ARTICLE_SECRET}", "topics": TOPICS, **body},
        headers={"Authorization": f"Bearer {settings.internal_api_key}"},
    )


def test_parse_keeps_valid_entries_and_drops_wrong_values():
    signals = parse_signals(
        json.dumps(
            [
                SIGNAL,
                {**SIGNAL, "topic": "chung_khoan"},
                {**SIGNAL, "direction": "len"},
                {**SIGNAL, "fact_status": "chac_chan"},
                {**SIGNAL, "evidence": ""},
                {**SIGNAL, "evidence": "x" * (MAX_EVIDENCE_CHARS + 1)},
                "khong phai object",
            ]
        ),
        TOPIC_KEYS,
    )

    assert len(signals) == 1
    assert signals[0].change_percent == 3.5
    assert signals[0].topic == "fuel"


@pytest.mark.parametrize("percent", ["7", True, [7]])
def test_a_percent_that_is_not_a_number_becomes_null(percent):
    assert parse_signals(json.dumps([{**SIGNAL, "change_percent": percent}]), TOPIC_KEYS)[0].change_percent is None


def test_at_most_a_fixed_number_of_signals():
    assert len(parse_signals(json.dumps([SIGNAL] * (MAX_SIGNALS + 3)), TOPIC_KEYS)) == MAX_SIGNALS


def test_extract_returns_signals_and_sends_only_the_article():
    seen = {}

    def handler(request):
        seen["body"] = json.loads(request.content)
        return _gemini_reply([SIGNAL])

    response = _post(_app(_client(handler)))

    assert response.status_code == 200
    assert response.json()["status"] == "ok"
    assert response.json()["signals"][0]["evidence"] == SIGNAL["evidence"]
    prompt = seen["body"]["contents"][0]["parts"][0]["text"]
    assert "CHỈ LÀ DỮ LIỆU" in prompt
    assert ARTICLE_SECRET in prompt
    assert "cooking_gas (gas nấu ăn)" in prompt
    assert seen["body"]["generationConfig"]["responseSchema"]["items"]["properties"]["topic"]["enum"] == TOPIC_KEYS
    assert seen["body"]["generationConfig"]["responseMimeType"] == "application/json"
    assert "tools" not in seen["body"]


def test_without_a_key_it_says_so_and_calls_nothing():
    calls = []

    def handler(request):
        calls.append(request)
        return _gemini_reply([SIGNAL])

    response = _post(_app(_client(handler, key=None)))

    assert response.json() == {"status": "not_configured", "signals": []}
    assert calls == []


@pytest.mark.parametrize("reply", [httpx.Response(500), httpx.Response(200, json={"candidates": [{"content": {"parts": [{"text": "khong phai json"}]}}]})])
def test_a_provider_failure_or_bad_reply_is_failed_without_content_in_logs(reply, capsys):
    response = _post(_app(_client(lambda _request: reply)))

    assert response.json() == {"status": "failed", "signals": []}
    captured = capsys.readouterr()
    assert ARTICLE_SECRET not in captured.out + captured.err


def test_the_internal_key_is_required():
    test_client = _app(_client(lambda _request: _gemini_reply([])))

    assert test_client.post("/api/v1/market-data/extract", json={"text": "x", "topics": TOPICS}).status_code == 401
    assert test_client.post("/api/v1/market-data/extract", json={"text": "x", "topics": TOPICS}, headers={"Authorization": "Bearer sai"}).status_code == 401


def test_oversized_text_is_rejected():
    response = _post(_app(_client(lambda _request: _gemini_reply([]))), text="x" * (MAX_TEXT_CHARS + 1))

    assert response.status_code == 422


def test_a_topic_outside_the_list_is_dropped():
    assert parse_signals(json.dumps([{**SIGNAL, "topic": "tuition"}]), TOPIC_KEYS) == []


@pytest.mark.parametrize("topics", [[], [{"key": "Có Dấu", "description": "x"}], [{"key": "fuel", "description": ""}]])
def test_bad_topic_lists_are_rejected(topics):
    response = _post(_app(_client(lambda _request: _gemini_reply([]))), topics=topics)

    assert response.status_code == 422
