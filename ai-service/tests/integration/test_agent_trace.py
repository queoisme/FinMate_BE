"""Vết lập luận (Task 9b): mỗi bước gate/plan/compose ghi MỘT dòng agent_runs khi backend gửi X-Trace-Id + X-User-Hash. Chỉ có tên bước, độ trễ, token, kết luận — không bao giờ nội dung câu hỏi hay số tiền."""

import hashlib
import uuid

import pytest
from sqlalchemy import text

from app.agent import gate, trace
from app.agent.llm.provider import LlmFailed, LlmResult
from app.api.v1 import agent as agent_api

USER_HASH = hashlib.sha256(b"user-for-trace").hexdigest()
QUESTION = "Mua xe 600 triệu sau 2 năm được không?"
EVIDENCE = [{"id": "balance", "key": "balance", "value_vnd": 100_000_000, "text": "Tổng số dư", "unit": "VND", "source": "confirmed", "as_of": "2026-11-07T03:00:00Z", "confidence": 0.9}]


class FakeProvider:
    def __init__(self, data, error=None):
        self.data, self.error = data, error

    async def generate_json(self, system, user, schema, max_output_tokens):
        if self.error:
            raise self.error
        return LlmResult(data=self.data, input_tokens=120, output_tokens=30, provider="fake", model="fake-1")


@pytest.fixture(autouse=True)
def fresh_gate_cache():
    gate.reset_cache()
    yield
    gate.reset_cache()


@pytest.fixture
def use(client):
    from app.main import app

    def apply(provider=None):
        app.dependency_overrides[agent_api.get_agent_provider] = lambda: provider
        app.dependency_overrides[agent_api.get_gate_nlu] = lambda: None

    yield apply
    app.dependency_overrides.pop(agent_api.get_agent_provider, None)
    app.dependency_overrides.pop(agent_api.get_gate_nlu, None)


def headers(auth_headers, trace_id=None, user_hash=USER_HASH):
    return {**auth_headers, **({"X-Trace-Id": str(trace_id)} if trace_id else {}), **({"X-User-Hash": user_hash} if user_hash else {})}


async def runs(session, trace_id=None):
    sql = "SELECT step, tool, latency_ms, input_tokens, output_tokens, verdict, user_id_hash, trace_id::text AS trace_id FROM agent_runs" + (" WHERE trace_id = CAST(:t AS uuid)" if trace_id else "") + " ORDER BY created_at"
    return (await session.execute(text(sql), {"t": str(trace_id)} if trace_id else {})).mappings().all()


async def test_each_step_of_one_turn_leaves_one_row_under_the_same_trace_id(client, auth_headers, session, use):
    trace_id = uuid.uuid4()
    use(FakeProvider({"intent": "forecast_goal", "steps": [], "clarify": "Giá bao nhiêu?"}))
    h = headers(auth_headers, trace_id)

    await client.post("/api/v1/agent/gate", json={"text": QUESTION}, headers=h)
    await client.post("/api/v1/agent/plan", json={"text": QUESTION, "tools": [{"name": "forecast_goal"}]}, headers=h)
    use(FakeProvider({"claims": [{"text": "Bạn có 100 triệu.", "evidence_ids": ["balance"]}]}))
    await client.post("/api/v1/agent/compose", json={"question": QUESTION, "evidence": EVIDENCE}, headers=h)

    rows = await runs(session, trace_id)
    assert [r["step"] for r in rows] == ["gate", "plan", "compose"]
    assert {r["user_id_hash"] for r in rows} == {USER_HASH}
    assert {r["trace_id"] for r in rows} == {str(trace_id)}
    assert [(r["input_tokens"], r["output_tokens"]) for r in rows] == [(0, 0), (120, 30), (120, 30)]
    assert rows[0]["verdict"] == "finance_cue"
    assert rows[1]["verdict"] == "clarify"
    assert rows[2]["verdict"] == "ok"
    assert all(r["latency_ms"] >= 0 for r in rows)


async def test_a_failed_llm_and_a_template_fallback_are_visible_in_the_verdict(client, auth_headers, session, use):
    trace_id = uuid.uuid4()
    use(FakeProvider({}, error=LlmFailed("http_503")))
    h = headers(auth_headers, trace_id)

    plan_response = await client.post("/api/v1/agent/plan", json={"text": QUESTION, "tools": [{"name": "forecast_goal"}]}, headers=h)
    compose_response = await client.post("/api/v1/agent/compose", json={"question": QUESTION, "evidence": EVIDENCE}, headers=h)

    assert plan_response.status_code == 503 and compose_response.status_code == 200
    assert [(r["step"], r["verdict"]) for r in await runs(session, trace_id)] == [("plan", "llm_unavailable"), ("compose", "template")]


async def test_without_the_trace_headers_nothing_is_recorded(client, auth_headers, session, use):
    use(FakeProvider({}))

    response = await client.post("/api/v1/agent/gate", json={"text": QUESTION}, headers=auth_headers)

    assert response.status_code == 200
    assert await runs(session) == []


@pytest.mark.parametrize("trace_id,user_hash", [("not-a-uuid", USER_HASH), (str(uuid.uuid4()), "3f2504e0-4f89-11d3-9a0c-0305e82c3301"), (str(uuid.uuid4()), "abc")])
async def test_malformed_trace_headers_are_ignored_and_never_break_the_request(client, auth_headers, session, use, trace_id, user_hash):
    use(FakeProvider({}))

    response = await client.post("/api/v1/agent/gate", json={"text": QUESTION}, headers={**auth_headers, "X-Trace-Id": trace_id, "X-User-Hash": user_hash})

    assert response.status_code == 200
    assert await runs(session) == []


async def test_no_row_contains_the_question_or_an_amount(client, auth_headers, session, use):
    trace_id = uuid.uuid4()
    use(FakeProvider({"claims": [{"text": "Bạn có 100 triệu.", "evidence_ids": ["balance"]}]}))
    h = headers(auth_headers, trace_id)

    await client.post("/api/v1/agent/gate", json={"text": QUESTION}, headers=h)
    await client.post("/api/v1/agent/compose", json={"question": QUESTION, "evidence": EVIDENCE}, headers=h)

    everything = " ".join(str(dict(r)) for r in await runs(session, trace_id))
    for forbidden in ("Mua xe", "600 triệu", "100 triệu", "Tổng số dư"):
        assert forbidden not in everything


async def test_a_failing_trace_write_never_breaks_the_answer(client, auth_headers, use, monkeypatch):
    use(FakeProvider({}))

    def boom(*args, **kwargs):
        raise RuntimeError("DB ghi vết hỏng")

    monkeypatch.setattr(trace, "AgentRun", boom)

    response = await client.post("/api/v1/agent/gate", json={"text": QUESTION}, headers=headers(auth_headers, uuid.uuid4()))

    assert response.status_code == 200
