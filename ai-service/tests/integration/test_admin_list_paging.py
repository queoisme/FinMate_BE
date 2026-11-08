"""Mọi danh sách quản trị phải do SERVER cắt trang và tìm kiếm: tổng khớp bộ lọc, các trang rời nhau, ``%`` gõ vào là chữ chứ không phải ký tự đại diện."""

import pytest
import pytest_asyncio
from sqlalchemy import delete

from app.bill import rules_admin as rules
from app.core.anonymizer import sha256_hex
from app.db.models.bill import BillAiInstruction
from app.services import method_prompts

ACTOR = sha256_hex("admin-1")
RULES = "/api/v1/admin/rules"


@pytest_asyncio.fixture
async def three_instructions(session):
    await session.execute(delete(BillAiInstruction))
    await session.commit()
    for text in ("Ưu tiên cửa hàng tiện lợi", "Ưu tiên quán ăn", "Chỉ dẫn 100% chính xác"):
        await rules.create_instruction(session, text, actor=ACTOR)


async def test_ai_instructions_page_and_search_on_the_server(client, auth_headers, three_instructions):
    first = (await client.get(f"{RULES}/ai-instructions", params={"limit": 2, "offset": 0}, headers=auth_headers)).json()
    second = (await client.get(f"{RULES}/ai-instructions", params={"limit": 2, "offset": 2}, headers=auth_headers)).json()

    assert (first["total"], len(first["items"]), first["limit"], first["offset"]) == (3, 2, 2, 0)
    assert len(second["items"]) == 1 and second["offset"] == 2
    assert {i["id"] for i in first["items"]}.isdisjoint(i["id"] for i in second["items"])

    found = (await client.get(f"{RULES}/ai-instructions", params={"q": "ƯU TIÊN QUÁN"}, headers=auth_headers)).json()
    assert found["total"] == 1 and "quán ăn" in found["items"][0]["instruction_text"]
    percent = (await client.get(f"{RULES}/ai-instructions", params={"q": "100%"}, headers=auth_headers)).json()
    assert percent["total"] == 1, "% là chữ"
    assert (await client.get(f"{RULES}/ai-instructions", params={"q": "%"}, headers=auth_headers)).json()["total"] == 1, "% đơn lẻ không được khớp mọi dòng"


async def test_history_can_be_searched_by_resource_key(client, auth_headers, three_instructions):
    everything = (await client.get(f"{RULES}/history", params={"resource": "instruction"}, headers=auth_headers)).json()
    key = everything["items"][0]["resource_key"]

    found = (await client.get(f"{RULES}/history", params={"q": key[:8]}, headers=auth_headers)).json()

    assert everything["total"] >= 3 and found["total"] >= 1
    assert all(key[:8].lower() in i["resource_key"].lower() for i in found["items"])
    assert (await client.get(f"{RULES}/history", params={"q": "khong-co-9876"}, headers=auth_headers)).json()["total"] == 0
    several = (await client.get(f"{RULES}/history", params={"resource": "instruction,brand"}, headers=auth_headers)).json()
    assert several["total"] == everything["total"], "resource nhiều giá trị là một danh sách chung do server gộp"


async def test_prompt_versions_page_and_keep_the_active_name_even_when_it_is_off_page(session, client, auth_headers):
    for version in ("method-select-p1", "method-select-p2", "method-select-p3"):
        await method_prompts.create_prompt(session, version, f"Chỉ dẫn {version}", activate=False)
    await method_prompts.activate_prompt(session, "method-select-p1", expected_active=None)  # bản cũ nhất đang dùng → nằm ngoài trang đầu

    page = (await client.get("/api/v1/admin/finance-method-prompts", params={"limit": 2}, headers=auth_headers)).json()

    assert page["total"] >= 3 and len(page["items"]) == 2 and page["limit"] == 2
    assert page["active_version"] == "method-select-p1"


async def test_rasa_model_ledger_reports_total_and_page(client, auth_headers):
    body = (await client.get("/api/v1/admin/rasa/models", params={"limit": 5}, headers=auth_headers)).json()

    assert body["limit"] == 5 and body["offset"] == 0 and body["total"] == len(body["items"])


async def test_provider_formats_search_by_package_or_provider_and_echo_paging(client, auth_headers):
    headers = {**auth_headers, "X-Actor-Hash": ACTOR}
    for key, package in (("alpha_bank", "com.alpha.bank"), ("beta_wallet", "com.beta.wallet")):
        created = await client.post("/api/v1/admin/provider-formats", json={"provider_key": key, "package_name": package}, headers=headers)
        assert created.status_code == 201, created.text

    by_package = (await client.get("/api/v1/admin/provider-formats", params={"q": "ALPHA", "limit": 5}, headers=auth_headers)).json()
    by_provider = (await client.get("/api/v1/admin/provider-formats", params={"q": "beta_wal"}, headers=auth_headers)).json()
    none = (await client.get("/api/v1/admin/provider-formats", params={"q": "khong-co-9876"}, headers=auth_headers)).json()

    assert by_package["total"] == 1 and by_package["limit"] == 5 and by_package["offset"] == 0
    assert by_provider["total"] == 1 and by_provider["items"][0]["provider_key"] == "beta_wallet"
    assert none["total"] == 0 and none["items"] == []
