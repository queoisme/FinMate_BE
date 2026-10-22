"""``POST /api/v1/bill/classify-items``: khóa nội bộ, kiểm tra đầu vào, không log tên món."""

import json

import httpx
import pytest
import structlog

from app.api.v1.bill import get_ai_instruction, get_bill_knowledge, get_gemini_client
from app.core.config import settings
from app.main import app

HASH = "a" * 64


class FakeKnowledge:
    async def refresh_if_stale(self):
        return None

    async def cached_items(self, keys):
        return {}

    async def remember_items(self, answers):
        return None


class FakeGemini:
    available = True

    def __init__(self):
        self.calls = []

    async def classify(self, names, instruction):
        self.calls.append(list(names))
        return ["shopping"] * len(names)


@pytest.fixture(autouse=True)
def gemini():
    fake = FakeGemini()
    app.dependency_overrides[get_bill_knowledge] = lambda: FakeKnowledge()
    app.dependency_overrides[get_gemini_client] = lambda: fake
    app.dependency_overrides[get_ai_instruction] = lambda: "INSTR-BIMAT"
    yield fake
    for dep in (get_bill_knowledge, get_gemini_client, get_ai_instruction):
        app.dependency_overrides.pop(dep, None)


async def post(body, headers=True):
    transport = httpx.ASGITransport(app=app)
    auth = {"Authorization": f"Bearer {settings.internal_api_key}"} if headers else {}
    payload = {"user_id_hash": HASH, "allow_ai": True, **body}
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        return await client.post("/api/v1/bill/classify-items", json=payload, headers=auth)


async def test_api_requires_internal_key():
    assert (await post({"items": [{"name": "Gạo", "parent": None}]}, headers=False)).status_code == 401


async def test_api_classifies_items():
    res = await post({"items": [{"name": "Gạo ST25", "parent": None}, {"name": "Qwerty", "parent": None},
                                {"name": "+ zz", "parent": 1}], "merchant_name": None})
    assert res.status_code == 200
    assert res.json() == {"ai_used": True, "items": [
        {"index": 0, "category_slug": "shopping", "source": "ai"},  # AI quyết trước luật
        {"index": 1, "category_slug": "shopping", "source": "ai"},
        {"index": 2, "category_slug": "shopping", "source": "inherit"},
    ]}


async def test_api_health_bill_goes_to_gemini_like_any_other(gemini):
    res = await post({"items": [{"name": "Paracetamol", "parent": None}, {"name": "Qwerty", "parent": None}],
                      "merchant_name": None})
    assert res.status_code == 200 and gemini.calls == [["Paracetamol", "Qwerty"]] and res.json()["ai_used"] is True


@pytest.mark.parametrize("items", [[], [{"name": "x", "parent": None}] * 61, [{"name": "x" * 201, "parent": None}]])
async def test_api_rejects_empty_or_oversized_items(items):
    assert (await post({"items": items})).status_code == 422


@pytest.mark.parametrize("parent", [0, 1, 5, -1])
async def test_api_rejects_parent_not_pointing_to_an_earlier_item(parent):
    items = [{"name": "a", "parent": None}, {"name": "b", "parent": parent}] if parent != 0 else [{"name": "a", "parent": 0}]
    assert (await post({"items": items})).status_code == 422


async def test_api_never_logs_item_names(gemini):
    with structlog.testing.capture_logs() as logs:
        res = await post({"items": [{"name": "TENMONBIMAT", "parent": None}], "merchant_name": "CUAHANGBIMAT"})
    assert res.status_code == 200 and gemini.calls == [["TENMONBIMAT"]]
    assert logs
    dump = json.dumps(logs, default=str)
    for secret in ("TENMONBIMAT", "CUAHANGBIMAT", "INSTR-BIMAT", "shopping"):
        assert secret not in dump


async def test_instruction_loader_returns_empty_and_caches_when_db_fails(monkeypatch):
    from app.api.v1 import bill

    calls = []

    def broken_session():
        calls.append(1)
        raise RuntimeError("relation bill_ai_instructions does not exist")

    monkeypatch.setattr(bill, "AsyncSessionLocal", broken_session)
    monkeypatch.setattr(bill, "_instruction", None)
    assert await bill.get_ai_instruction() == ""
    assert await bill.get_ai_instruction() == ""
    assert calls == [1]  # lần sau trong 60 giây không chạm DB
