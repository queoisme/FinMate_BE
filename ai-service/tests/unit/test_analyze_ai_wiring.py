"""``Pipeline.analyze`` nối bước AI sau Categorizer, trước dò trùng; route dựng AiDeps từ các dependency (không DB: repo và detector được thay bằng bản giả)."""

import time
import uuid
from datetime import UTC, datetime

import httpx
import pytest

from app.api.deps import get_db
from app.api.v1.bill import get_ai_instruction, get_bill_knowledge, get_chat_gemini_client
from app.core.config import settings
from app.main import app
from app.pipeline import orchestrator
from app.pipeline.models.model_registry import ModelRegistry
from app.pipeline.stages import ai_categorizer, duplicate_detector
from app.pipeline.stages.ai_categorizer import AiDeps
from app.schemas.request import AnalyzeRequest
from app.schemas.response import AnalyzeResponse, ClassifierResult, DuplicateResult, ModelVersions
from tests.unit.test_item_classifier import FakeGemini, FakeKnowledge
from tests.unit.test_notification_ai_category import RULE_OTHER, extraction

DEBIT = "TK 1234 GD: -1,000,000VND luc 29-10-2026 09:15. SD: 4,915,000VND. ND: mua quan ao"
CREDIT = "TK 1234 GD: +1,000,000VND luc 29-10-2026 09:15. SD: 4,915,000VND. ND: nhan tien mua quan ao"


def make_request(body=DEBIT, allow_ai=None):
    data = {
        "backend_request_id": str(uuid.uuid4()), "user_id_hash": "a" * 64, "package_name": "com.unknown.bank",
        "notification_title": "Bank", "notification_body": body, "received_at": datetime(2026, 10, 29, 9, 15, tzinfo=UTC).isoformat(),
    }
    if allow_ai is not None:
        data["allow_ai"] = allow_ai
    return AnalyzeRequest.model_validate(data)


@pytest.fixture
def pipe(monkeypatch):
    order: list[str] = []

    async def none(*a, **k):
        return None

    async def add(session, row):
        return row

    async def detect(*a, **k):
        order.append("detect")
        return duplicate_detector.DuplicateOutcome(False)

    p = orchestrator.Pipeline(ModelRegistry())
    monkeypatch.setattr(p, "refresh", none)
    monkeypatch.setattr(orchestrator.pipeline_repo, "get_by_backend_request_id", none)
    monkeypatch.setattr(orchestrator.pipeline_repo, "add", add)
    monkeypatch.setattr(duplicate_detector, "detect", detect)
    monkeypatch.setattr(settings, "collect_raw_samples", False)
    p.order = order
    return p


def deps(gemini):
    return AiDeps(gemini, FakeKnowledge(), "LUAT")


async def test_allow_ai_with_fake_ai_categorizes_the_narrative(pipe):
    gemini = FakeGemini(lambda n: "shopping")
    got = await pipe.analyze(None, make_request(allow_ai=True), deps(gemini))
    assert got.extraction.description == "mua quan ao"
    assert (got.categorization.category_slug, got.categorization.confidence) == ("shopping", 0.70)
    assert gemini.calls == [["mua quan ao"]]


async def test_without_allow_ai_the_ai_is_never_called(pipe):
    gemini = FakeGemini(lambda n: "shopping")
    got = await pipe.analyze(None, make_request(), deps(gemini))
    assert got.categorization.category_slug == "other" and gemini.calls == []


async def test_ai_failure_still_returns_other(pipe):
    got = await pipe.analyze(None, make_request(allow_ai=True), deps(FakeGemini(fail=True)))
    assert got.pipeline_result == "financial" and got.categorization.category_slug == "other"


async def test_credit_is_income_and_ai_is_not_called(pipe):
    gemini = FakeGemini(lambda n: "shopping")
    got = await pipe.analyze(None, make_request(CREDIT, allow_ai=True), deps(gemini))
    assert got.categorization.category_slug == "income" and gemini.calls == []


async def test_ai_none_keeps_the_old_behaviour(pipe):
    got = await pipe.analyze(None, make_request(allow_ai=True))
    assert got.categorization.category_slug == "other"


async def test_the_ai_step_runs_before_duplicate_detection(pipe, monkeypatch):
    real = orchestrator.categorize_with_ai

    async def spy(*a, **k):
        pipe.order.append("ai")
        return await real(*a, **k)

    monkeypatch.setattr(orchestrator, "categorize_with_ai", spy)
    await pipe.analyze(None, make_request(allow_ai=True), deps(FakeGemini(lambda n: "shopping")))
    assert pipe.order == ["ai", "detect"]


async def test_a_20k_char_description_is_capped_and_fast():
    gemini = FakeGemini(lambda n: "shopping")
    started = time.perf_counter()
    await ai_categorizer.categorize_with_ai(RULE_OTHER, extraction("mua " + "1 " * 10_000), True, deps(gemini), timeout=1.0)
    assert time.perf_counter() - started < 0.5
    assert all(len(n) <= 300 for call in gemini.calls for n in call)


async def test_route_builds_ai_deps_from_the_overridden_dependencies(monkeypatch):
    seen = {}
    gemini, knowledge = FakeGemini(), FakeKnowledge()

    class Session:
        async def commit(self):
            pass

    async def db():
        yield Session()

    async def instruction():
        return "CHI DAN"

    async def fake_analyze(session, request, ai=None):
        seen["ai"], seen["allow"] = ai, request.allow_ai
        return AnalyzeResponse(
            pipeline_result="non_financial", classifier=ClassifierResult(label="non_financial", confidence=0.1), extraction=None,
            categorization=None, duplicate=DuplicateResult(is_potential_duplicate=False, duplicate_request_id=None),
            model_versions=ModelVersions(classifier=None, extractor=None, categorizer=None), processing_ms=1,
        )

    monkeypatch.setattr(orchestrator.pipeline, "analyze", fake_analyze)
    overrides = {get_db: db, get_chat_gemini_client: lambda: gemini, get_bill_knowledge: lambda: knowledge, get_ai_instruction: instruction}
    app.dependency_overrides.update(overrides)
    try:
        async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://t") as http:
            response = await http.post(
                "/api/v1/analyze", json=make_request(allow_ai=True).model_dump(mode="json"),
                headers={"Authorization": f"Bearer {settings.internal_api_key}"},
            )
    finally:
        for dep in overrides:
            app.dependency_overrides.pop(dep, None)
    assert response.status_code == 200
    assert seen["allow"] is True and seen["ai"] == AiDeps(gemini, knowledge, "CHI DAN")
