"""Hợp đồng JSON backend ↔ ai-service của vòng agent (Task 9). Các tệp ở tests/fixtures/agent_wire dùng CHUNG với test C# ``AgentAiClientContractTests``:
request là JSON backend sinh ra (ở đây phải parse được bằng đúng các model ``extra="forbid"``, không trường lạ); response là JSON model Python sinh ra (backend phải đọc được)."""

import json
from pathlib import Path

import pytest

from app.agent.schemas import ComposeRequest, ComposeResponse, Evidence, GateRequest, GateResponse, MemoryDeleted, MemoryEntry, MemoryPutRequest, PlanRequest, PlanResponse

FIXTURES = Path(__file__).resolve().parents[1] / "fixtures" / "agent_wire"


def load(name: str) -> dict:
    return json.loads((FIXTURES / name).read_text(encoding="utf-8"))


@pytest.mark.parametrize("name,model", [("gate_request.json", GateRequest), ("plan_request.json", PlanRequest), ("compose_request.json", ComposeRequest)])
def test_the_backend_requests_are_accepted_by_the_models_without_unknown_fields(name, model):
    parsed = model.model_validate(load(name))

    assert parsed is not None


@pytest.mark.parametrize("name,model", [("gate_response.json", GateResponse), ("plan_response.json", PlanResponse), ("compose_response.json", ComposeResponse)])
def test_the_response_fixtures_are_exactly_what_the_models_produce(name, model):
    fixture = load(name)

    assert model.model_validate(fixture).model_dump(mode="json") == fixture


def test_the_plan_request_carries_what_the_planner_needs():
    request = PlanRequest.model_validate(load("plan_request.json"))

    assert [t.name for t in request.tools] == ["forecast_goal", "get_assumptions"]
    assert request.tools[0].args_schema["price_vnd"]["type"] == "int"
    assert request.segment == "variable"
    assert request.max_steps == 4
    assert [(f.tool, f.code) for f in request.feedback] == [("forecast_goal", "invalid_argument:price_vnd")]
    assert request.state.last_intent == "forecast_goal" and request.state.slots == {"price_vnd": "600000000"}
    assert [(m.key, m.value) for m in request.memory] == [("preferred_method", "phong bì")]


def test_the_compose_request_keeps_money_as_integers_and_sources_as_wire_names():
    request = ComposeRequest.model_validate(load("compose_request.json"))

    assert request.evidence[0].value_vnd == 100_000_000 and request.evidence[0].source == "confirmed"
    assert request.evidence[1].value_vnd is None and request.evidence[1].source_name == "Tổng cục Thống kê"
    assert request.skip_verifier is False


def test_the_memory_request_and_responses_match_the_models():
    put = MemoryPutRequest.model_validate(load("memory_put_request.json"))

    assert put.value == "phong bì"
    assert len(put.user_id_hash) == 64  # mã băm SHA-256, không phải id thật
    assert MemoryEntry.model_validate(load("memory_put_response.json")).model_dump(mode="json") == load("memory_put_response.json")
    assert [MemoryEntry.model_validate(i).model_dump(mode="json") for i in load("memory_list_response.json")] == load("memory_list_response.json")
    assert MemoryDeleted.model_validate(load("memory_deleted_response.json")).deleted == 2


def test_no_wire_fixture_carries_a_user_identity_except_the_memory_hash():
    for path in FIXTURES.glob("*.json"):
        text = path.read_text(encoding="utf-8").lower().replace("user_id_hash", "") if path.name.startswith("memory_") else path.read_text(encoding="utf-8").lower()
        assert "user" not in text, path.name


def test_the_knowledge_requests_and_responses_match_the_models():
    from app.api.v1.kb import CandidateRecord, SearchRequest

    assert SearchRequest.model_validate(load("kb_search_request.json")).k == 3
    assert CandidateRecord.model_validate(load("kb_candidate_request.json")).text.endswith("?")
    response = load("kb_search_response.json")
    evidence = [Evidence.model_validate(e) for e in response["evidence"]]
    assert evidence[0].source == "document" and evidence[0].value_vnd is None and evidence[0].source_name == "Sổ tay FinMate"
    assert [e.model_dump(mode="json") for e in evidence] == [{**e, "as_of": e["as_of"].replace("Z", "Z")} for e in response["evidence"]]
    assert load("kb_candidate_response.json") == {"recorded": True}


def test_the_ticket_assist_request_and_response_match_the_models():
    from app.agent.ticket_assist import TicketAssistRequest, TicketAssistResponse

    request = TicketAssistRequest.model_validate(load("ticket_assist_request.json"))
    assert request.history[0].role == "staff"
    response = load("ticket_assist_response.json")
    assert TicketAssistResponse.model_validate(response).model_dump(mode="json") == response
