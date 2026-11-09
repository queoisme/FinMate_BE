"""Cổng phạm vi trên Postgres thật: câu từ chối và cụm từ lấy từ DB (admin sửa được), gieo mặc định chỉ thêm — DB thắng; hai route gate/plan của agent."""

import pytest
from sqlalchemy import text

from app.agent import gate
from app.agent.llm.provider import LlmFailed, LlmResult
from app.api.deps import get_db
from app.api.v1 import agent as agent_api


@pytest.fixture(autouse=True)
def fresh_gate_cache():
    gate.reset_cache()
    yield
    gate.reset_cache()


class FakeProvider:
    def __init__(self, data=None, error=None):
        self.data, self.error = data, error

    async def generate_json(self, system, user, schema, max_output_tokens):
        if self.error:
            raise self.error
        return LlmResult(data=self.data, input_tokens=10, output_tokens=5, provider="fake", model="fake-1")


@pytest.fixture
def override(client):
    """Cho từng test đặt nhà cung cấp LLM và Rasa giả; dọn khi xong (``client`` đã dọn dependency_overrides của chính nó)."""
    from app.main import app

    def apply(provider=None, nlu=None):
        app.dependency_overrides[agent_api.get_agent_provider] = lambda: provider
        app.dependency_overrides[agent_api.get_gate_nlu] = lambda: nlu

    yield apply
    app.dependency_overrides.pop(agent_api.get_agent_provider, None)
    app.dependency_overrides.pop(agent_api.get_gate_nlu, None)


async def test_seed_defaults_adds_cues_and_templates_once(session):
    first = await gate.seed_defaults(session)
    second = await gate.seed_defaults(session)

    assert first["cues"] > 0 and first["refusals"] > 0
    assert second == {"cues": 0, "refusals": 0}
    assert (await session.execute(text("SELECT count(*) FROM agent_scope_cues WHERE phrase <> lower(phrase)"))).scalar_one() == 0  # lưu dạng đã bỏ dấu, chữ thường


async def test_seeding_never_overwrites_what_an_admin_changed(session):
    await gate.seed_defaults(session)
    await session.execute(text("UPDATE agent_refusal_templates SET text = 'Câu do admin viết' WHERE code = 'out_of_scope'"))
    await session.commit()

    await gate.seed_defaults(session)

    assert (await session.execute(text("SELECT text FROM agent_refusal_templates WHERE code = 'out_of_scope'"))).scalar_one() == "Câu do admin viết"


async def test_gate_route_returns_the_refusal_text_stored_in_the_db(client, auth_headers, session, override):
    override(nlu=None)
    await gate.seed_defaults(session)
    await session.execute(text("UPDATE agent_refusal_templates SET text = 'Chỉ nói chuyện tiền thôi nhé' WHERE code = 'out_of_scope'"))
    await session.commit()

    response = await client.post("/api/v1/agent/gate", json={"text": "Thời tiết hôm nay"}, headers=auth_headers)

    assert response.status_code == 200
    assert response.json() == {"in_scope": False, "reason_code": "out_of_scope", "refusal_text": "Chỉ nói chuyện tiền thôi nhé"}


async def test_gate_route_passes_a_forecast_question_and_blocks_investment_advice(client, auth_headers, session, override):
    override(nlu=None)
    await gate.seed_defaults(session)

    ok = await client.post("/api/v1/agent/gate", json={"text": "Mua xe 600 triệu sau 2 năm được không?"}, headers=auth_headers)
    blocked = await client.post("/api/v1/agent/gate", json={"text": "Nên mua coin nào?"}, headers=auth_headers)

    assert ok.json() == {"in_scope": True, "reason_code": "finance_cue", "refusal_text": None}
    assert blocked.json()["reason_code"] == "investment_advice"
    assert blocked.json()["refusal_text"]


async def test_gate_works_with_an_empty_db_by_using_the_built_in_defaults(client, auth_headers, override):
    override(nlu=None)

    response = await client.post("/api/v1/agent/gate", json={"text": "Thời tiết hôm nay"}, headers=auth_headers)

    assert response.json()["in_scope"] is False
    assert response.json()["refusal_text"]


async def test_agent_routes_require_the_internal_api_key(client):
    for path, body in (("/api/v1/agent/gate", {"text": "x"}), ("/api/v1/agent/plan", {"text": "x", "tools": [{"name": "t"}]})):
        assert (await client.post(path, json=body)).status_code in (401, 403)


async def test_plan_route_returns_steps_and_usage(client, auth_headers, override):
    override(provider=FakeProvider({"intent": "forecast_goal", "steps": [{"tool": "forecast_goal", "args_json": '{"price_vnd": 600000000, "horizon_months": 24}'}], "clarify": ""}))

    response = await client.post(
        "/api/v1/agent/plan",
        json={"text": "Mua xe 600 triệu sau 2 năm được không?", "tools": [{"name": "forecast_goal"}], "segment": "variable"},
        headers=auth_headers,
    )

    body = response.json()
    assert response.status_code == 200
    assert body["steps"] == [{"tool": "forecast_goal", "args": {"price_vnd": 600000000, "horizon_months": 24}}]
    assert body["clarify"] is None
    assert body["usage"] == {"input_tokens": 10, "output_tokens": 5, "provider": "fake", "model": "fake-1"}


async def test_plan_route_is_503_with_a_fixed_code_when_the_llm_fails(client, auth_headers, override):
    override(provider=FakeProvider(error=LlmFailed("http_503")))

    response = await client.post("/api/v1/agent/plan", json={"text": "mua xe 600 triệu", "tools": [{"name": "forecast_goal"}]}, headers=auth_headers)

    assert response.status_code == 503
    assert response.json() == {"detail": {"code": "llm_unavailable", "reason": "http_503"}}


async def test_plan_route_rejects_a_request_without_tools(client, auth_headers, override):
    override(provider=FakeProvider({}))

    response = await client.post("/api/v1/agent/plan", json={"text": "mua xe", "tools": []}, headers=auth_headers)

    assert response.status_code == 422
