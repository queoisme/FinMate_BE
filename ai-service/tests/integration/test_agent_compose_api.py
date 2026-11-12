"""Route ``POST /api/v1/agent/compose``: LLM hỏng thì vẫn 200 với câu mẫu; bộ kiểm chứng chỉ tắt được khi ``AGENT_EVAL_ENABLED``."""

import pytest

from app.agent.llm.provider import LlmFailed, LlmResult
from app.api.v1 import agent as agent_api
from app.core.config import settings

EVIDENCE = [
    {"id": "balance", "key": "balance", "value_vnd": 100_000_000, "text": "Tổng số dư", "unit": "VND", "source": "confirmed", "as_of": "2026-11-07T03:00:00Z", "confidence": 0.9},
]
BODY = {"question": "Mình còn bao nhiêu tiền?", "evidence": EVIDENCE}


class FakeProvider:
    def __init__(self, claims=None, error=None):
        self.claims, self.error, self.calls = claims, error, 0

    async def generate_json(self, system, user, schema, max_output_tokens):
        self.calls += 1
        if self.error:
            raise self.error
        return LlmResult(data={"claims": self.claims}, input_tokens=300, output_tokens=40, provider="fake", model="fake-1")


@pytest.fixture
def use_provider(client):
    from app.main import app

    def apply(provider):
        app.dependency_overrides[agent_api.get_agent_provider] = lambda: provider

    yield apply
    app.dependency_overrides.pop(agent_api.get_agent_provider, None)


async def test_a_grounded_answer_comes_back_with_usage(client, auth_headers, use_provider):
    use_provider(FakeProvider([{"text": "Bạn đang có 100 triệu.", "evidence_ids": ["balance"]}]))

    response = await client.post("/api/v1/agent/compose", json=BODY, headers=auth_headers)

    body = response.json()
    assert response.status_code == 200
    assert body["claims"] == [{"text": "Bạn đang có 100 triệu.", "evidence_ids": ["balance"]}]
    assert body["used_template"] is False
    assert body["usage"] == {"input_tokens": 300, "output_tokens": 40, "provider": "fake", "model": "fake-1"}


async def test_an_llm_outage_still_answers_from_the_evidence(client, auth_headers, use_provider):
    use_provider(FakeProvider(error=LlmFailed("http_503")))

    response = await client.post("/api/v1/agent/compose", json=BODY, headers=auth_headers)

    body = response.json()
    assert response.status_code == 200
    assert body["used_template"] is True
    assert "100.000.000 đồng" in body["claims"][0]["text"]
    assert body["usage"] is None


async def test_no_evidence_answers_that_there_is_no_data_without_calling_the_llm(client, auth_headers, use_provider):
    provider = FakeProvider([])
    use_provider(provider)

    response = await client.post("/api/v1/agent/compose", json={"question": "còn bao nhiêu?", "evidence": []}, headers=auth_headers)

    assert response.json()["no_data"] is True
    assert provider.calls == 0


async def test_skip_verifier_is_ignored_outside_eval_mode(client, auth_headers, use_provider, monkeypatch):
    monkeypatch.setattr(settings, "agent_eval_enabled", False)
    invented = FakeProvider([{"text": "Bạn đang có 999 triệu.", "evidence_ids": ["balance"]}])
    use_provider(invented)

    response = await client.post("/api/v1/agent/compose", json={**BODY, "skip_verifier": True}, headers=auth_headers)

    assert response.json()["used_template"] is True  # số bịa vẫn bị chặn dù người gọi xin bỏ kiểm chứng
    assert "999" not in response.json()["claims"][0]["text"]


async def test_skip_verifier_is_honoured_in_eval_mode(client, auth_headers, use_provider, monkeypatch):
    monkeypatch.setattr(settings, "agent_eval_enabled", True)
    use_provider(FakeProvider([{"text": "Bạn đang có 999 triệu.", "evidence_ids": ["balance"]}]))

    response = await client.post("/api/v1/agent/compose", json={**BODY, "skip_verifier": True}, headers=auth_headers)

    assert response.json()["used_template"] is False
    assert "999 triệu" in response.json()["claims"][0]["text"]


async def test_compose_requires_the_internal_api_key_and_rejects_unknown_fields(client, auth_headers):
    assert (await client.post("/api/v1/agent/compose", json=BODY)).status_code in (401, 403)
    assert (await client.post("/api/v1/agent/compose", json={**BODY, "user_id": "3f2504e0-4f89-11d3-9a0c-0305e82c3301"}, headers=auth_headers)).status_code == 422
