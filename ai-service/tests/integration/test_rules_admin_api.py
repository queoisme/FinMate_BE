"""HTTP của API quản trị luật (``/api/v1/admin/rules``): khóa nội bộ, tiêu đề người thực hiện, ánh xạ lỗi, và mọi thay đổi có hiệu lực ngay."""

import pytest
import pytest_asyncio
from sqlalchemy import delete

from app.api.v1 import bill as bill_api
from app.api.v1.bill import get_bill_knowledge
from app.db.models.bill import BillAiInstruction
from app.main import app

ACTOR = "b" * 64
BASE = "/api/v1/admin/rules"


class FakeKnowledge:
    def __init__(self):
        self.stale_marks = 0

    async def refresh_if_stale(self):
        return None

    def mark_stale(self):
        self.stale_marks += 1


@pytest.fixture
def knowledge():
    fake = FakeKnowledge()
    app.dependency_overrides[get_bill_knowledge] = lambda: fake
    yield fake
    app.dependency_overrides.pop(get_bill_knowledge, None)


@pytest_asyncio.fixture(autouse=True)
async def no_instructions(session):
    await session.execute(delete(BillAiInstruction))
    await session.commit()


@pytest.fixture
def write_headers(auth_headers):
    return {**auth_headers, "X-Actor-Hash": ACTOR}


async def test_every_route_needs_the_internal_key(client):
    for method, path in [("GET", "/brands"), ("GET", "/keywords"), ("GET", "/history"), ("GET", "/meta"), ("GET", "/ai-instructions"), ("GET", "/item-cache")]:
        assert (await client.request(method, BASE + path)).status_code == 401


async def test_writes_need_the_actor_header(client, auth_headers):
    response = await client.put(f"{BASE}/brands/zeta%20mart", headers=auth_headers, json={"category_slug": "groceries"})
    assert response.status_code == 422
    bad = await client.put(f"{BASE}/brands/zeta%20mart", headers={**auth_headers, "X-Actor-Hash": "not-a-hash"}, json={"category_slug": "groceries"})
    assert bad.status_code == 422


async def test_meta_lists_the_valid_values(client, auth_headers):
    body = (await client.get(f"{BASE}/meta", headers=auth_headers)).json()
    assert "dining" in body["category_groups"] and "income" not in body["backend_slugs"] and body["keyword_kinds"] == ["merchant", "product"]


async def test_brand_lifecycle_and_the_rules_are_refreshed_at_once(client, auth_headers, write_headers, knowledge):
    created = await client.put(f"{BASE}/brands/Zeta%20Mart", headers=write_headers, json={"category_slug": "groceries", "display_name": "Zeta", "reason": "thêm"})
    assert created.status_code == 200 and created.json()["brand_key"] == "zeta mart" and knowledge.stale_marks == 1
    listed = (await client.get(f"{BASE}/brands", headers=auth_headers, params={"q": "zeta"})).json()
    assert listed["total"] == 1 and listed["items"][0]["category_slug"] == "groceries" and listed["limit"] == 50 and listed["offset"] == 0
    off = await client.patch(f"{BASE}/brands/zeta%20mart/active", headers=write_headers, json={"is_active": False})
    assert off.json()["is_active"] is False and knowledge.stale_marks == 2
    history = (await client.get(f"{BASE}/history", headers=auth_headers, params={"resource": "brand"})).json()
    assert [h["action"] for h in history["items"]] == ["deactivate", "create"] and history["items"][1]["reason"] == "thêm"


async def test_errors_are_json_with_a_code_and_do_not_refresh_anything(client, write_headers, knowledge):
    invalid = await client.put(f"{BASE}/brands/x", headers=write_headers, json={"category_slug": "income"})
    assert invalid.status_code == 422 and invalid.json()["error"]["code"] == "invalid_category"
    missing = await client.patch(f"{BASE}/brands/khong%20co/active", headers=write_headers, json={"is_active": False})
    assert missing.status_code == 404 and missing.json()["error"]["code"] == "not_found"
    assert knowledge.stale_marks == 0


async def test_keyword_create_returns_warnings_and_conflicts_are_409(client, auth_headers, write_headers, knowledge):
    first = await client.post(f"{BASE}/keywords", headers=write_headers, json={"kind": "product", "category_slug": "dining", "keyword": "lau"})
    assert first.status_code == 201 and [w["code"] for w in first.json()["warnings"]] == ["short_keyword"]
    again = await client.post(f"{BASE}/keywords", headers=write_headers, json={"kind": "product", "category_slug": "dining", "keyword": "lau"})
    assert again.status_code == 409 and again.json()["error"]["code"] == "duplicate"
    keyword_id = first.json()["id"]
    off = await client.patch(f"{BASE}/keywords/{keyword_id}/active", headers=write_headers, json={"is_active": False})
    assert off.status_code == 200 and off.json()["is_active"] is False
    assert (await client.get(f"{BASE}/keywords", headers=auth_headers, params={"active": "false"})).json()["total"] == 1
    assert (await client.patch(f"{BASE}/keywords/not-a-uuid/active", headers=write_headers, json={"is_active": True})).status_code == 422


async def test_item_cache_put_list_and_delete(client, auth_headers, write_headers, knowledge):
    put = await client.put(f"{BASE}/item-cache", headers=write_headers, json={"name": "Bỉm cho bé 200.000", "category_slug": "family"})
    assert put.status_code == 200 and put.json()["item_key"] == "bim cho be"
    assert (await client.get(f"{BASE}/item-cache", headers=auth_headers, params={"q": "bim"})).json()["total"] == 1
    gone = await client.delete(f"{BASE}/item-cache", headers=write_headers, params={"name": "bim cho be"})
    assert gone.status_code == 204
    assert (await client.delete(f"{BASE}/item-cache", headers=write_headers, params={"name": "bim cho be"})).status_code == 404


async def test_instruction_create_activate_and_the_cache_is_dropped(client, auth_headers, write_headers, knowledge):
    bill_api._instruction = (123.0, "cũ")
    first = (await client.post(f"{BASE}/ai-instructions", headers=write_headers, json={"instruction_text": "Chỉ dẫn 1"})).json()
    assert bill_api._instruction is None  # lần gọi Gemini kế tiếp đọc lại chỉ dẫn mới ngay
    await client.post(f"{BASE}/ai-instructions", headers=write_headers, json={"instruction_text": "Chỉ dẫn 2"})
    listed = (await client.get(f"{BASE}/ai-instructions", headers=auth_headers)).json()
    assert [i["instruction_text"] for i in listed["items"]] == ["Chỉ dẫn 2", "Chỉ dẫn 1"]
    back = await client.post(f"{BASE}/ai-instructions/{first['id']}/activate", headers=write_headers)
    assert back.status_code == 200 and back.json()["is_active"] is True
    assert (await client.post(f"{BASE}/ai-instructions", headers=write_headers, json={"instruction_text": "  "})).status_code == 422


async def test_preview_shows_current_and_proposed_and_the_item_cache(client, write_headers, auth_headers, knowledge):
    await client.put(f"{BASE}/item-cache", headers=write_headers, json={"name": "zqfoo", "category_slug": "shopping"})
    body = {
        "merchant_name": "Qwerty Zzz", "lines": ["zqfoo 10.000", "zqfoo 20.000"], "item_name": "zqfoo",
        "proposed_keywords": [{"kind": "product", "category_slug": "shopping", "keyword": "zqfoo"}],
    }
    marks_before = knowledge.stale_marks
    result = (await client.post(f"{BASE}/preview", headers=auth_headers, json=body)).json()  # đọc thử, không cần tiêu đề người thực hiện
    assert result["changed"] is True and result["proposed"]["bill"]["category_slug"] == "shopping"
    assert result["current"]["item"]["rule_slug"] is None and result["current"]["item"]["cache_slug"] == "shopping"
    assert knowledge.stale_marks == marks_before  # thử không đổi gì
    bad = await client.post(f"{BASE}/preview", headers=auth_headers, json={"proposed_keywords": [{"kind": "chat", "category_slug": "dining", "keyword": "abc"}]})
    assert bad.status_code == 422 and bad.json()["error"]["code"] == "invalid_kind"


async def test_history_revert_over_http(client, auth_headers, write_headers, knowledge):
    await client.put(f"{BASE}/brands/Alpha%20Shop", headers=write_headers, json={"category_slug": "shopping"})
    entry = (await client.get(f"{BASE}/history", headers=auth_headers)).json()["items"][0]
    marks = knowledge.stale_marks
    reverted = await client.post(f"{BASE}/history/{entry['id']}/revert", headers=write_headers, json={"reason": "nhầm"})
    assert reverted.status_code == 200 and reverted.json()["state"]["is_active"] is False and knowledge.stale_marks == marks + 1
    assert (await client.post(f"{BASE}/history/00000000-0000-0000-0000-000000000000/revert", headers=write_headers, json={})).status_code == 404


@pytest.mark.parametrize("params", [{"limit": 0}, {"limit": 1000}, {"offset": -1}])
async def test_paging_limits_are_enforced(client, auth_headers, params):
    assert (await client.get(f"{BASE}/brands", headers=auth_headers, params=params)).status_code == 422
