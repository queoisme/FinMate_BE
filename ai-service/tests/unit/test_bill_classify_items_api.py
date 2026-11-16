"""``POST /api/v1/bill/classify-items``: khóa nội bộ, kiểm tra đầu vào, không log tên món."""

import json
import uuid

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

    async def user_item_memory(self, user_id_hash, keys):
        return {}


class FakeGemini:
    available = True

    def __init__(self):
        self.calls = []
        self.among_calls = []

    async def classify(self, names, instruction):
        self.calls.append(list(names))
        return ["shopping"] * len(names)

    async def classify_among(self, names, choices, instruction):
        self.among_calls.append((list(names), list(choices)))
        return [len(choices) - 1] * len(names)  # luôn chọn danh mục cuối trong danh sách


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
        {"index": 0, "category_slug": "shopping", "category_id": None, "source": "ai"},  # AI quyết trước luật
        {"index": 1, "category_slug": "shopping", "category_id": None, "source": "ai"},
        {"index": 2, "category_slug": "shopping", "category_id": None, "source": "inherit"},
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


FOOD, MILK_TEA = uuid.uuid4(), uuid.uuid4()
CATEGORIES = [
    {"id": str(FOOD), "name": "Ăn uống", "slug": "food", "is_system": True},
    {"id": str(MILK_TEA), "name": "TENDANHMUCBIMAT", "slug": "custom-1", "is_system": False, "parent_slug": "food"},
]


async def test_api_passes_categories_and_returns_category_id(gemini):
    res = await post({"items": [{"name": "Qwerty", "parent": None}, {"name": "+ zz", "parent": 0}], "categories": CATEGORIES})

    assert res.status_code == 200
    assert res.json() == {"ai_used": True, "items": [
        {"index": 0, "category_slug": "custom-1", "category_id": str(MILK_TEA), "source": "ai"},
        {"index": 1, "category_slug": "custom-1", "category_id": str(MILK_TEA), "source": "inherit"},
    ]}
    assert gemini.calls == [] and [names for names, _ in gemini.among_calls] == [["Qwerty"]]


async def test_api_rejects_more_than_60_categories():
    many = [{"id": str(uuid.uuid4()), "name": f"c{i}", "slug": f"custom-{i}", "is_system": False} for i in range(61)]
    assert (await post({"items": [{"name": "Gạo", "parent": None}], "categories": many})).status_code == 422


async def test_api_without_categories_still_uses_the_old_path(gemini):
    res = await post({"items": [{"name": "Qwerty", "parent": None}]})
    assert res.status_code == 200 and gemini.calls == [["Qwerty"]] and gemini.among_calls == []


async def test_api_response_always_carries_the_category_id_key():
    # Không có id (đường cũ, hoặc món không nhận ra) thì khóa vẫn có, giá trị null: backend không phải đoán khóa thiếu.
    res = await post({"items": [{"name": "Qwerty", "parent": None}], "allow_ai": False})
    assert res.status_code == 200 and res.json()["items"] == [{"index": 0, "category_slug": None, "category_id": None, "source": "unknown"}]


async def test_api_never_logs_item_or_category_names(gemini):
    with structlog.testing.capture_logs() as logs:
        res = await post({"items": [{"name": "TENMONBIMAT", "parent": None}], "merchant_name": "CUAHANGBIMAT", "categories": CATEGORIES})
    assert res.status_code == 200 and [names for names, _ in gemini.among_calls] == [["TENMONBIMAT"]]
    assert logs
    dump = json.dumps(logs, default=str)
    for secret in ("TENMONBIMAT", "CUAHANGBIMAT", "TENDANHMUCBIMAT", "INSTR-BIMAT", str(MILK_TEA)):
        assert secret not in dump


async def test_instruction_loader_returns_empty_and_caches_when_db_fails(monkeypatch):
    from app.api.v1 import bill

    calls = []

    def broken_session():
        calls.append(1)
        raise RuntimeError("relation kb_ai_instructions does not exist")

    monkeypatch.setattr(bill, "AsyncSessionLocal", broken_session)
    monkeypatch.setattr(bill, "_instruction", None)
    assert await bill.get_ai_instruction() == ""
    assert await bill.get_ai_instruction() == ""
    assert calls == [1]  # lần sau trong 60 giây không chạm DB
