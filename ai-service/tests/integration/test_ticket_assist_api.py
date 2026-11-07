"""Route ``POST /api/v1/agent/ticket-assist``: chỉ gợi ý; LLM hỏng thì 503 để khiếu nại vẫn xử lý được không cần trợ lý."""

import pytest

from app.agent.llm.provider import LlmFailed, LlmResult
from app.api.v1 import agent as agent_api

BODY = {"subject": "Bị trừ tiền hai lần", "body": "Mình bị trừ 150.000 đồng hai lần.", "history": []}


class FakeProvider:
    def __init__(self, data=None, error=None):
        self.data, self.error = data, error

    async def generate_json(self, system, user, schema, max_output_tokens):
        if self.error:
            raise self.error
        return LlmResult(data=self.data, input_tokens=10, output_tokens=5, provider="fake", model="fake-1")


@pytest.fixture
def use_provider(client):
    from app.main import app

    def apply(provider):
        app.dependency_overrides[agent_api.get_agent_provider] = lambda: provider

    yield apply
    app.dependency_overrides.pop(agent_api.get_agent_provider, None)


async def test_a_grounded_suggestion_is_returned(client, auth_headers, use_provider):
    use_provider(FakeProvider({"summary": "Bạn bị trừ 150.000 đồng hai lần.", "suggested_reply": "Mình đã ghi nhận khoản 150.000 đồng bị trừ hai lần."}))

    response = await client.post("/api/v1/agent/ticket-assist", json=BODY, headers=auth_headers)

    assert response.status_code == 200
    assert response.json()["verified"] is True and response.json()["usage"]["model"] == "fake-1"


async def test_an_llm_failure_is_503_with_a_fixed_code(client, auth_headers, use_provider):
    use_provider(FakeProvider(error=LlmFailed("http_503")))

    response = await client.post("/api/v1/agent/ticket-assist", json=BODY, headers=auth_headers)

    assert response.status_code == 503 and response.json()["detail"]["code"] == "llm_unavailable"


async def test_the_route_needs_the_internal_key_and_rejects_unknown_fields(client, auth_headers):
    assert (await client.post("/api/v1/agent/ticket-assist", json=BODY)).status_code == 401
    assert (await client.post("/api/v1/agent/ticket-assist", json={**BODY, "user_id": "x"}, headers=auth_headers)).status_code == 422
