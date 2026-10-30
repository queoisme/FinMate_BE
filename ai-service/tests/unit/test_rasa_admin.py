"""Quản trị Rasa: chỉ đọc trạng thái và thử một câu; Rasa tắt thì trả ``available: false`` chứ không ném lỗi."""
import asyncio

import httpx
import pytest

from app.api.v1 import rasa_admin


def _use(monkeypatch, handler, url="http://rasa:5005"):
    monkeypatch.setattr(rasa_admin.settings, "rasa_url", url)
    monkeypatch.setattr(rasa_admin, "_client", lambda: httpx.AsyncClient(transport=httpx.MockTransport(handler)))


def test_status_reads_version_and_model(monkeypatch):
    def handler(request):
        if request.url.path == "/version":
            return httpx.Response(200, json={"version": "3.6.21"})
        return httpx.Response(200, json={"model_file": "nlu.tar.gz", "model_id": "abc", "num_active_training_jobs": 0})

    _use(monkeypatch, handler)
    assert asyncio.run(rasa_admin.status()) == {
        "configured": True, "available": True, "version": "3.6.21", "model_file": "nlu.tar.gz", "model_id": "abc", "num_active_training_jobs": 0,
    }


def test_status_without_rasa_url_is_not_configured(monkeypatch):
    monkeypatch.setattr(rasa_admin.settings, "rasa_url", None)
    assert asyncio.run(rasa_admin.status()) == {"configured": False, "available": False}


def test_status_when_rasa_is_down_is_unavailable_not_an_error(monkeypatch):
    def handler(request):
        raise httpx.ConnectError("down")

    _use(monkeypatch, handler)
    assert asyncio.run(rasa_admin.status()) == {"configured": True, "available": False}


def test_parse_returns_intent_top_ranking_and_entities(monkeypatch):
    ranking = [{"name": f"i{n}", "confidence": 1 / (n + 1)} for n in range(8)]

    def handler(request):
        assert request.url.path == "/model/parse"
        return httpx.Response(200, json={"intent": ranking[0], "intent_ranking": ranking, "entities": [{"entity": "amount", "value": "45k", "start": 0}]})

    _use(monkeypatch, handler)
    result = asyncio.run(rasa_admin.parse(rasa_admin.ParseBody(text="an pho 45k")))
    assert result["intent"]["name"] == "i0"
    assert len(result["intent_ranking"]) == rasa_admin.MAX_RANKING
    assert result["entities"] == [{"entity": "amount", "value": "45k"}]  # chỉ tên và giá trị, bỏ vị trí ký tự


def test_parse_rejects_empty_and_oversized_text():
    with pytest.raises(ValueError):
        rasa_admin.ParseBody(text="")
    with pytest.raises(ValueError):
        rasa_admin.ParseBody(text="x" * 501)
