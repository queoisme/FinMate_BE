"""Admin quản lý phiên bản chỉ dẫn AI cho gợi ý phương thức (``app/services/method_prompts.py``, ``/admin/finance-method-prompts``): Postgres thật.

Cái cần giữ: luôn đúng MỘT bản đang dùng; không sửa tại chỗ (đổi = thêm bản mới, bản cũ còn); bản mới có hiệu lực ngay (bỏ cache); quay về bản cũ được."""

import pytest
import pytest_asyncio
from sqlalchemy import select, text

from app.bill.rules_admin import RuleError
from app.db.models.method_prompt import MethodPrompt
from app.services import method_picker, method_prompts
from app.services.method_prompt_seed import METHOD_PROMPT_TEXT, METHOD_PROMPT_VERSION

ACTOR = "b" * 64


@pytest_asyncio.fixture(autouse=True)
async def prompts_table(engine):
    """``kb_method_prompts`` không nằm trong TRUNCATE của conftest (migration gieo sẵn một dòng mà ``test_migration_0030`` kiểm): bảng rỗng trước mỗi test của file này, rồi khôi phục dòng gieo sẵn."""

    async def reset(restore_seed: bool) -> None:
        async with engine.begin() as connection:
            await connection.execute(text("DELETE FROM kb_method_prompts"))
            if restore_seed:
                await connection.execute(
                    text("INSERT INTO kb_method_prompts (prompt_version, instruction_text, is_active) VALUES (:version, :text, true)"),
                    {"version": METHOD_PROMPT_VERSION, "text": METHOD_PROMPT_TEXT},
                )

    await reset(False)
    method_picker.reset_method_prompt_cache()
    yield
    await reset(True)
    method_picker.reset_method_prompt_cache()


async def seed(session, version="method-select-v1", active=True):
    session.add(MethodPrompt(prompt_version=version, instruction_text=f"Chỉ dẫn {version}", is_active=active))
    await session.commit()


async def active_versions(session):
    session.expire_all()
    return [r.prompt_version for r in (await session.scalars(select(MethodPrompt).where(MethodPrompt.is_active.is_(True)))).all()]


async def test_a_new_version_is_added_without_changing_the_active_one(session):
    await seed(session)
    created = await method_prompts.create_prompt(session, "method-select-v2", "## Cách cân nhắc\n1. Thử nghiệm", activate=False)
    assert created["prompt_version"] == "method-select-v2" and created["is_active"] is False
    assert await active_versions(session) == ["method-select-v1"]
    listed = await method_prompts.list_prompts(session)
    assert listed["active_version"] == "method-select-v1" and {i["prompt_version"] for i in listed["items"]} == {"method-select-v1", "method-select-v2"}


async def test_creating_with_activate_switches_in_one_step_and_keeps_the_old_row(session):
    await seed(session)
    await method_prompts.create_prompt(session, "method-select-v2", "Chỉ dẫn mới", activate=True)
    assert await active_versions(session) == ["method-select-v2"]
    assert (await session.scalar(select(MethodPrompt).where(MethodPrompt.prompt_version == "method-select-v1"))).instruction_text == "Chỉ dẫn method-select-v1"  # bản cũ nguyên vẹn để truy ngược


async def test_an_existing_version_is_never_edited_in_place(session):
    await seed(session)
    with pytest.raises(RuleError) as caught:
        await method_prompts.create_prompt(session, "method-select-v1", "Cố ý ghi đè", activate=True)
    assert (caught.value.status, caught.value.code) == (409, "prompt_version_exists")
    session.expire_all()
    assert (await session.scalar(select(MethodPrompt))).instruction_text == "Chỉ dẫn method-select-v1"


@pytest.mark.parametrize(
    ("version", "text", "code"),
    [("co khoang trang", "x", "invalid_prompt_version"), ("v" * 51, "x", "invalid_prompt_version"), ("ok-v2", "   ", "empty_instruction"), ("ok-v2", "x" * 8001, "instruction_too_long")],
)
async def test_invalid_input_is_refused_with_a_clear_code(session, version, text, code):
    await seed(session)
    with pytest.raises(RuleError) as caught:
        await method_prompts.create_prompt(session, version, text, activate=False)
    assert (caught.value.status, caught.value.code) == (422, code)
    assert len((await method_prompts.list_prompts(session))["items"]) == 1


async def test_switching_back_to_an_older_version_works_and_the_stale_guard_protects_other_admins(session):
    await seed(session)
    await method_prompts.create_prompt(session, "method-select-v2", "Chỉ dẫn mới", activate=True)
    with pytest.raises(RuleError) as caught:
        await method_prompts.activate_prompt(session, "method-select-v1", expected_active="method-select-v1")  # người sửa nhìn thấy bản cũ, thực tế đã đổi
    assert caught.value.code == "stale_active"
    result = await method_prompts.activate_prompt(session, "method-select-v1", expected_active="method-select-v2")
    assert result == {"active_version": "method-select-v1", "previous_version": "method-select-v2"}
    assert await active_versions(session) == ["method-select-v1"]


@pytest.mark.parametrize(("version", "status", "code"), [("method-select-v1", 409, "already_active"), ("khong-co", 404, "prompt_not_found")])
async def test_activating_the_current_or_an_unknown_version_is_refused(session, version, status, code):
    await seed(session)
    with pytest.raises(RuleError) as caught:
        await method_prompts.activate_prompt(session, version, expected_active=None)
    assert (caught.value.status, caught.value.code) == (status, code)


async def test_a_new_active_version_takes_effect_immediately_not_after_the_cache_expires(session):
    await seed(session)
    method_picker._prompt = (10**12, method_picker.MethodPromptRow("method-select-v1", "cũ"))  # cache còn rất hạn
    await method_prompts.create_prompt(session, "method-select-v2", "Chỉ dẫn mới", activate=True)
    assert method_picker._prompt is None  # bị bỏ: lần nạp tiếp theo đọc DB


async def test_http_routes_need_the_internal_key_and_an_actor_for_writes(client, session, auth_headers):
    await seed(session)
    assert (await client.get("/api/v1/admin/finance-method-prompts")).status_code in (401, 403)
    listed = await client.get("/api/v1/admin/finance-method-prompts", headers=auth_headers)
    assert listed.status_code == 200 and listed.json()["active_version"] == "method-select-v1"
    body = {"prompt_version": "method-select-v2", "instruction_text": "Chỉ dẫn mới", "activate": True}
    assert (await client.post("/api/v1/admin/finance-method-prompts", json=body, headers=auth_headers)).status_code == 422  # thiếu người thực hiện
    created = await client.post("/api/v1/admin/finance-method-prompts", json=body, headers={**auth_headers, "X-Actor-Hash": ACTOR})
    assert created.status_code == 201 and created.json()["is_active"] is True
    back = await client.post("/api/v1/admin/finance-method-prompts/method-select-v1/activate", json={"expected_active": "method-select-v2"}, headers={**auth_headers, "X-Actor-Hash": ACTOR})
    assert back.status_code == 200 and back.json()["active_version"] == "method-select-v1"
    duplicate = await client.post("/api/v1/admin/finance-method-prompts", json=body, headers={**auth_headers, "X-Actor-Hash": ACTOR})
    assert duplicate.status_code == 409 and duplicate.json()["error"]["code"] == "prompt_version_exists"
