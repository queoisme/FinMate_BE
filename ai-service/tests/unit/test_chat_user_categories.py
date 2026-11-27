"""``POST /api/v1/chat/parse`` với ``categories``: khoản chi được xếp vào danh mục của CHÍNH người dùng (kể cả danh mục tự tạo) bằng bộ phân loại món của hóa đơn.

Tình huống thật (2026-11-26): người dùng ẩn "Ăn uống" (hệ thống) và tự tạo "Đồ Uống", "Đồ ăn"; "mua cà phê 30k và bánh 20k" ra "Chưa phân loại" vì chat chỉ biết slug hệ thống ``food``.
"""

import uuid

import httpx
import pytest

from app.api.v1 import chat as chat_route
from app.api.v1.bill import get_ai_instruction, get_bill_knowledge, get_chat_gemini_client
from app.bill.gemini import GeminiFailed
from app.bill.knowledge import MemoryHit
from app.core.config import settings
from app.main import app

HASH = "a" * 64
DO_UONG, DO_AN, KHAC = uuid.uuid4(), uuid.uuid4(), uuid.uuid4()
CATEGORIES = [
    {"id": str(KHAC), "name": "Khác", "slug": "other", "is_system": True, "parent_slug": None},
    {"id": str(DO_UONG), "name": "Đồ Uống", "slug": "do-uong", "is_system": False, "parent_slug": "food"},  # "Ăn uống" bị ẩn nên không có trong danh sách
    {"id": str(DO_AN), "name": "Đồ ăn", "slug": "do-an", "is_system": False, "parent_slug": "food"},
]


class FakeKnowledge:
    def __init__(self, memory=None):
        self.memory = memory or {}

    async def refresh_if_stale(self):
        return None

    async def cached_items(self, keys):
        return {}

    async def remember_items(self, answers):
        raise AssertionError("chat không được ghi vào bảng nhớ dùng chung")

    async def user_item_memory(self, user_id_hash, keys):
        return {k: v for k, v in self.memory.items() if k in keys}


class FakeGemini:
    available = True

    def __init__(self, fail=False):
        self.fail, self.calls, self.system_calls = fail, [], []

    async def classify(self, names, instruction):
        self.system_calls.append(list(names))
        return ["unknown"] * len(names)

    async def classify_among(self, names, choices, instruction):
        self.calls.append((list(names), list(choices)))
        if self.fail:
            raise GeminiFailed("boom")
        pick = {"cà phê": "Đồ Uống", "bánh": "Đồ ăn"}
        wanted = [next(value for key, value in pick.items() if key in name) for name in names]
        return [next(i for i, label in enumerate(choices) if label.startswith(prefix)) for prefix in wanted]


@pytest.fixture
def setup():
    state = {"knowledge": FakeKnowledge(), "gemini": FakeGemini()}
    chat_route.set_nlu(None)
    app.dependency_overrides[get_bill_knowledge] = lambda: state["knowledge"]
    app.dependency_overrides[get_chat_gemini_client] = lambda: state["gemini"]
    app.dependency_overrides[get_ai_instruction] = lambda: "INSTR"
    yield state
    for dep in (get_bill_knowledge, get_chat_gemini_client, get_ai_instruction):
        app.dependency_overrides.pop(dep, None)
    chat_route.set_nlu(None)


async def post(text, **extra):
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        return (
            await client.post(
                "/api/v1/chat/parse", json={"text": text, "user_id_hash": HASH, **extra}, headers={"Authorization": f"Bearer {settings.internal_api_key}"}
            )
        ).json()


async def test_without_categories_the_answer_is_the_system_slug_as_before(setup):
    entry = (await post("mua cà phê 30k", allow_ai=True))["entry"]
    assert (entry["category_slug"], entry["category_id"]) == ("food", None)
    assert setup["gemini"].calls == []


async def test_with_ai_consent_gemini_picks_the_users_own_category_as_a_suggestion_only(setup):
    body = await post("mua cà phê 30k và bánh 20k", allow_ai=True, categories=CATEGORIES)

    picked = [(e["description"], e["category_slug"], e["category_id"]) for e in body["entries"]]
    assert picked == [("mua cà phê", "do-uong", str(DO_UONG)), ("bánh", "do-an", str(DO_AN))]
    assert all(e["category_source"] == "cache" and e["auto_save_ok"] is False for e in body["entries"])  # AI chọn: chỉ gợi ý, người dùng xác nhận
    assert body["entry"] == body["entries"][0]
    assert setup["gemini"].system_calls == [] and len(setup["gemini"].calls) == 1  # MỘT vòng Gemini, không còn bước chọn danh mục hệ thống chạy trước


async def test_a_remembered_choice_wins_without_asking_gemini_and_may_save_directly(setup):
    from app.bill.item_classifier import item_key

    setup["knowledge"].memory = {item_key("mua cà phê"): MemoryHit(DO_UONG, "corrected")}

    entry = (await post("mua cà phê 30k", allow_ai=True, categories=CATEGORIES))["entry"]

    assert (entry["category_slug"], entry["category_id"], entry["category_source"], entry["auto_save_ok"]) == ("do-uong", str(DO_UONG), "rule", True)
    assert setup["gemini"].calls == []


async def test_without_ai_consent_nothing_is_sent_out_and_the_old_answer_stays(setup):
    entry = (await post("mua cà phê 30k", allow_ai=False, categories=CATEGORIES))["entry"]

    assert (entry["category_slug"], entry["category_id"]) == ("food", None)  # "Ăn uống" bị ẩn nên không ánh xạ được: để backend xử lý như cũ
    assert setup["gemini"].calls == []


async def test_a_failing_gemini_never_breaks_the_chat(setup):
    setup["gemini"] = FakeGemini(fail=True)

    response = await post("mua cà phê 30k", allow_ai=True, categories=CATEGORIES)

    assert response["intent"] == "add_expense" and response["entry"]["category_id"] is None


async def test_income_and_transfers_are_left_alone(setup):
    entry = (await post("nhận lương 14 củ 5", allow_ai=True, categories=CATEGORIES))["entry"]
    assert (entry["category_slug"], entry["category_id"]) == ("income", None)
    assert setup["gemini"].calls == []


async def test_more_than_sixty_categories_is_rejected(setup):
    many = [{"id": str(uuid.uuid4()), "name": f"C{i}", "slug": f"c{i}", "is_system": False} for i in range(61)]
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        res = await client.post(
            "/api/v1/chat/parse", json={"text": "ăn phở 50k", "user_id_hash": HASH, "categories": many}, headers={"Authorization": f"Bearer {settings.internal_api_key}"}
        )
    assert res.status_code == 422
