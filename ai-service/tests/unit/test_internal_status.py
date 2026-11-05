"""Trạng thái nhà cung cấp cho backend giám sát: đếm lỗi liên tiếp đúng, cần khóa nội bộ, không lộ khóa."""
import asyncio
import json

import httpx
import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

from app.api.v1 import internal_status, rasa_admin
from app.bill import provider_health
from app.bill.gemini import GeminiClient, GeminiFailed
from app.bill.provider_health import ProviderHealth

SECRET = "khoa-gemini-bi-mat"


@pytest.fixture(autouse=True)
def fresh_health(monkeypatch):
    primary, backup = ProviderHealth(), ProviderHealth()
    monkeypatch.setattr(provider_health, "gemini_health", primary)
    monkeypatch.setattr(provider_health, "backup_health", backup)
    from app.bill import gemini as gemini_module

    monkeypatch.setattr(gemini_module, "gemini_health", primary)
    monkeypatch.setattr(gemini_module, "backup_health", backup)
    monkeypatch.setattr(internal_status, "gemini_health", primary)
    monkeypatch.setattr(internal_status, "backup_health", backup)
    return primary, backup


def _client(handler, backup=False):
    return GeminiClient(
        SECRET,
        "model-a",
        1.0,
        transport=httpx.MockTransport(handler),
        backup_key="khoa-du-phong" if backup else None,
        backup_model="model-b" if backup else "",
    )


def _ok(names):
    text = json.dumps([{"index": i, "category": "unknown"} for i in range(len(names))])
    return httpx.Response(200, json={"candidates": [{"content": {"parts": [{"text": text}]}}]})


def test_failures_count_up_then_a_success_resets(fresh_health):
    primary, _ = fresh_health
    status = {"code": 403}

    def handler(_request):
        return httpx.Response(status["code"]) if status["code"] != 200 else _ok(["a"])

    for name in ("a", "b"):
        client = _client(handler)  # client mới: bỏ qua cầu dao, để đếm đúng từng lần gọi thật
        with pytest.raises(GeminiFailed):
            asyncio.run(client.classify([name], "chi dan"))

    assert primary.consecutive_failures == 2
    assert primary.last_error_code == "http_403"

    status["code"] = 200
    asyncio.run(_client(handler).classify(["a"], "chi dan"))
    assert primary.consecutive_failures == 0
    assert primary.last_success_at is not None


def test_a_healthy_backup_does_not_hide_a_failing_primary(fresh_health):
    primary, backup = fresh_health

    def handler(request):
        if "openrouter" in str(request.url):
            content = json.dumps([{"index": 0, "category": "unknown"}])
            return httpx.Response(200, json={"choices": [{"message": {"content": content}}]})
        return httpx.Response(429)

    client = _client(handler, backup=True)
    try:
        asyncio.run(client.classify(["a"], "chi dan"))
    except GeminiFailed:
        pass

    assert primary.consecutive_failures >= 1


def test_one_clients_success_does_not_hide_anothers_failures():
    health = ProviderHealth()
    for _ in range(3):
        health.failure("http_404", "bill")
        health.success("chat")

    assert health.consecutive_failures == 3

    health.success("bill")
    assert health.consecutive_failures == 0


def test_generate_text_failures_and_successes_are_counted(fresh_health):
    primary, _ = fresh_health
    with pytest.raises(GeminiFailed):
        asyncio.run(_client(lambda _r: httpx.Response(500)).generate_text("loi nhac", {"type": "OBJECT"}))
    assert primary.consecutive_failures == 1

    ok = httpx.Response(200, json={"candidates": [{"content": {"parts": [{"text": "{}"}]}}]})
    asyncio.run(_client(lambda _r: ok).generate_text("loi nhac", {"type": "OBJECT"}))
    assert primary.consecutive_failures == 0


def test_snapshot_never_contains_a_key_and_reports_disabled():
    health = ProviderHealth()
    health.failure("http_429")

    assert SECRET not in json.dumps(health.snapshot(True))
    assert health.snapshot(True)["state"] == "enabled"
    assert health.snapshot(False)["state"] == "disabled"


def test_status_endpoint_requires_the_internal_key(monkeypatch):
    monkeypatch.setattr(rasa_admin.settings, "rasa_url", None)
    app = FastAPI()
    app.include_router(internal_status.router, prefix="/api/v1")
    client = TestClient(app, raise_server_exceptions=False)

    assert client.get("/api/v1/internal/status").status_code in (401, 403)


def test_status_endpoint_reports_each_provider_without_secrets(monkeypatch):
    monkeypatch.setattr(rasa_admin.settings, "rasa_url", None)
    monkeypatch.setattr(internal_status, "get_gemini_client", lambda: _client(lambda _r: httpx.Response(500)))

    body = asyncio.run(internal_status.internal_status())

    assert body["gemini"]["state"] == "enabled"
    assert body["gemini_backup"]["state"] == "disabled"
    assert body["rasa"] == {"configured": False, "available": False}
    assert SECRET not in json.dumps(body)
