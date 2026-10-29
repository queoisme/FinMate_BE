"""Tài liệu cấu hình có phiên bản trên Postgres thật: kích hoạt, khóa lạc quan, hoàn tác, cổng đo, và cách một instance khác nạp lại từ DB."""

import copy

import pytest
import pytest_asyncio
from sqlalchemy import delete, func, select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import async_sessionmaker

from app.bill.rules_admin import RuleError
from app.db.models.config_document import ConfigDocument
from app.services import config_documents as docs
from app.services import config_runtime, intent_cues

ACTOR = "b" * 64


@pytest_asyncio.fixture(autouse=True)
async def clean(session):
    """Bảng này không nằm trong danh sách dọn chung của conftest; và bản đang chạy trong tiến trình phải trả về mặc định sau mỗi ca."""
    original = intent_cues.current()
    await session.execute(delete(ConfigDocument))
    await session.commit()
    docs.APPLIED.clear()
    yield
    intent_cues.apply(original)
    docs.APPLIED.clear()
    await session.execute(delete(ConfigDocument))
    await session.commit()


def edited(reply: str = "Câu trả lời do admin đổi.") -> dict:
    content = copy.deepcopy(intent_cues.default_data())
    content["replies"]["out_of_scope"] = reply
    return content


def worse() -> dict:
    content = copy.deepcopy(intent_cues.default_data())
    content["fragments"]["sell"] = "(?!x)x"  # "bán" không còn là dấu hiệu tiền vào: độ chính xác tụt
    return content


async def active_rows(session, name="intent_cues"):
    return (await session.scalars(select(ConfigDocument).where(ConfigDocument.name == name, ConfigDocument.status == "active"))).all()


# ---------------------------------------------------------------- bản nháp

async def test_a_draft_is_stored_but_changes_nothing_until_activated(session):
    saved = await docs.create_draft(session, "intent_cues", edited(), note="thử đổi câu trả lời", actor=ACTOR)
    assert saved["version"] == 1 and saved["status"] == "draft" and saved["validation"] == "ok"
    assert saved["diff_vs_active"]["changed"] == ["replies"]
    current = await docs.active_document(session, "intent_cues")
    assert current["version"] == 0 and current["source"] == "file"  # chưa ai kích hoạt: vẫn là bản mặc định
    assert intent_cues.current().replies["out_of_scope"] != "Câu trả lời do admin đổi."


async def test_versions_increase_and_a_note_is_required(session):
    await docs.create_draft(session, "intent_cues", edited("a"), note="lần một", actor=ACTOR)
    second = await docs.create_draft(session, "intent_cues", edited("b"), note="lần hai", actor=ACTOR)
    assert second["version"] == 2
    with pytest.raises(RuleError) as info:
        await docs.create_draft(session, "intent_cues", edited("c"), note=" ", actor=ACTOR)
    assert info.value.code == "note_required"
    listing = await docs.list_versions(session, "intent_cues")
    assert [v["version"] for v in listing["items"]] == [2, 1] and listing["total"] == 2


async def test_an_invalid_draft_is_refused_and_nothing_is_stored(session):
    broken = copy.deepcopy(intent_cues.default_data())
    del broken["patterns"]["fee"]
    with pytest.raises(RuleError) as info:
        await docs.create_draft(session, "intent_cues", broken, note="xóa mẫu phí", actor=ACTOR)
    assert info.value.status == 422 and info.value.code == "missing_keys"
    assert await session.scalar(select(func.count()).select_from(ConfigDocument)) == 0


# ---------------------------------------------------------------- kích hoạt, khóa lạc quan, hoàn tác

async def test_activating_applies_the_document_in_this_process_and_archives_the_previous_one(session):
    first = await docs.create_draft(session, "intent_cues", edited("Bản một."), note="một", actor=ACTOR)
    result = await docs.activate(session, "intent_cues", first["version"], actor=ACTOR, expected_active=0)
    assert result["status"] == "active" and result["previous_active"] == 0 and result["applied_here"]
    assert intent_cues.current().replies["out_of_scope"] == "Bản một."
    assert docs.APPLIED["intent_cues"] == 1

    second = await docs.create_draft(session, "intent_cues", edited("Bản hai."), note="hai", actor=ACTOR)
    await docs.activate(session, "intent_cues", second["version"], actor=ACTOR, expected_active=1)
    assert intent_cues.current().replies["out_of_scope"] == "Bản hai."
    rows = {r.version: r.status for r in (await session.scalars(select(ConfigDocument))).all()}
    assert rows == {1: "archived", 2: "active"}  # đúng một bản đang chạy


async def test_activating_while_someone_else_changed_it_is_refused(session):
    one = await docs.create_draft(session, "intent_cues", edited("Một."), note="một", actor=ACTOR)
    two = await docs.create_draft(session, "intent_cues", edited("Hai."), note="hai", actor=ACTOR)
    await docs.activate(session, "intent_cues", one["version"], actor=ACTOR, expected_active=0)
    with pytest.raises(RuleError) as info:  # người thứ hai vẫn nghĩ bản đang chạy là mặc định (0)
        await docs.activate(session, "intent_cues", two["version"], actor=ACTOR, expected_active=0)
    assert info.value.status == 409 and info.value.code == "stale_active"
    assert intent_cues.current().replies["out_of_scope"] == "Một."  # không bị ghi đè


async def test_reverting_is_activating_an_older_version(session):
    one = await docs.create_draft(session, "intent_cues", edited("Một."), note="một", actor=ACTOR)
    two = await docs.create_draft(session, "intent_cues", edited("Hai."), note="hai", actor=ACTOR)
    await docs.activate(session, "intent_cues", one["version"], actor=ACTOR, expected_active=0)
    await docs.activate(session, "intent_cues", two["version"], actor=ACTOR, expected_active=1)
    await docs.activate(session, "intent_cues", 1, actor=ACTOR, expected_active=2)  # hoàn tác
    assert intent_cues.current().replies["out_of_scope"] == "Một."
    assert [r.version for r in await active_rows(session)] == [1]


async def test_activating_the_running_version_again_is_a_conflict(session):
    one = await docs.create_draft(session, "intent_cues", edited(), note="một", actor=ACTOR)
    await docs.activate(session, "intent_cues", one["version"], actor=ACTOR, expected_active=0)
    with pytest.raises(RuleError) as info:
        await docs.activate(session, "intent_cues", 1, actor=ACTOR, expected_active=1)
    assert info.value.code == "already_active"


async def test_the_database_itself_refuses_two_active_versions(session):
    for version in (1, 2):
        session.add(ConfigDocument(name="thresholds", version=version, status="active", content={}, created_by=ACTOR))
    with pytest.raises(IntegrityError):
        await session.commit()
    await session.rollback()


# ---------------------------------------------------------------- cổng đo

async def test_a_version_that_makes_chat_worse_is_blocked_unless_forced(session):
    bad = await docs.create_draft(session, "intent_cues", worse(), note="thử bỏ động từ bán", actor=ACTOR)
    preview = await docs.preview_version(session, "intent_cues", bad["version"])
    assert preview["preview"]["available"] and preview["preview"]["delta"] < 0
    with pytest.raises(RuleError) as info:
        await docs.activate(session, "intent_cues", bad["version"], actor=ACTOR, expected_active=0)
    assert info.value.status == 422 and info.value.code == "preview_regression"
    assert await active_rows(session) == []  # bị chặn thì không có gì đổi
    await docs.activate(session, "intent_cues", bad["version"], actor=ACTOR, expected_active=0, force=True)
    assert [r.version for r in await active_rows(session)] == [bad["version"]]


async def test_documents_used_by_other_services_have_no_local_preview(session):
    draft = await docs.create_draft(session, "thresholds", docs.spec_of("thresholds").default(), note="giữ nguyên để thử luồng", actor=ACTOR)
    result = await docs.preview_version(session, "thresholds", draft["version"])
    assert result["preview"]["available"] is False
    activated = await docs.activate(session, "thresholds", draft["version"], actor=ACTOR, expected_active=0)
    assert activated["applied_here"] is False  # ocr-service tự nạp từ DB


# ---------------------------------------------------------------- một instance khác nạp lại từ DB

async def test_another_process_picks_up_the_active_document_on_its_next_refresh(session, engine):
    factory = async_sessionmaker(engine, expire_on_commit=False)
    session.add(ConfigDocument(name="intent_cues", version=1, status="active", content=edited("Từ instance khác."), created_by=ACTOR))
    await session.commit()
    assert intent_cues.current().replies["out_of_scope"] != "Từ instance khác."  # tiến trình này chưa biết
    assert await config_runtime.refresh(factory) == {"intent_cues": 1}
    assert intent_cues.current().replies["out_of_scope"] == "Từ instance khác."
    assert await config_runtime.refresh(factory) == {}  # không có gì mới thì không nạp lại


async def test_a_broken_document_in_the_database_never_takes_chat_down(session, engine):
    factory = async_sessionmaker(engine, expire_on_commit=False)
    before = intent_cues.current()
    broken = copy.deepcopy(intent_cues.default_data())
    broken["patterns"]["fee"]["regex"] = "(chua dong"
    session.add(ConfigDocument(name="intent_cues", version=1, status="active", content=broken, created_by=ACTOR))
    await session.commit()
    assert await config_runtime.refresh(factory) == {}  # bỏ qua bản hỏng
    assert intent_cues.current() is before  # vẫn chạy bản cũ
    assert docs.APPLIED.get("intent_cues", 0) == 0


async def test_removing_the_active_row_returns_the_process_to_the_default(session, engine):
    factory = async_sessionmaker(engine, expire_on_commit=False)
    session.add(ConfigDocument(name="intent_cues", version=1, status="active", content=edited("Tạm thời."), created_by=ACTOR))
    await session.commit()
    await config_runtime.refresh(factory)
    assert intent_cues.current().replies["out_of_scope"] == "Tạm thời."
    await session.execute(delete(ConfigDocument))
    await session.commit()
    assert await config_runtime.refresh(factory) == {"intent_cues": 0}
    assert intent_cues.current().replies["out_of_scope"] == intent_cues.default_data()["replies"]["out_of_scope"]


# ---------------------------------------------------------------- API

async def test_the_admin_api_walks_the_whole_flow(client, auth_headers):
    headers = {**auth_headers, "X-Actor-Hash": ACTOR}
    listing = (await client.get("/api/v1/admin/config/documents", headers=auth_headers)).json()["items"]
    assert {d["name"] for d in listing} == {"intent_cues", "chat_dictionary", "merchant_lexicon", "narrative_labels", "parsing_vocab", "thresholds", "app_screens"}

    current = (await client.get("/api/v1/admin/config/documents/intent_cues", headers=auth_headers)).json()
    assert current["version"] == 0 and current["source"] == "file"

    body = {"content": edited("Qua API."), "note": "thử qua API"}
    created = await client.post("/api/v1/admin/config/documents/intent_cues/drafts", json=body, headers=headers)
    assert created.status_code == 201 and created.json()["version"] == 1

    refused = await client.post("/api/v1/admin/config/documents/intent_cues/drafts", json={"content": {"x": 1}, "note": "sai"}, headers=headers)
    assert refused.status_code == 422 and refused.json()["error"]["code"] in {"invalid_section", "missing_keys"}

    stale = await client.post("/api/v1/admin/config/documents/intent_cues/versions/1/activate", json={"expected_active_version": 5}, headers=headers)
    assert stale.status_code == 409 and stale.json()["error"]["code"] == "stale_active"

    activated = await client.post("/api/v1/admin/config/documents/intent_cues/versions/1/activate", json={"expected_active_version": 0}, headers=headers)
    assert activated.status_code == 200 and activated.json()["status"] == "active"
    assert (await client.get("/api/v1/admin/config/documents/intent_cues", headers=auth_headers)).json()["version"] == 1


async def test_the_admin_api_needs_the_internal_key_and_an_actor(client, auth_headers):
    assert (await client.get("/api/v1/admin/config/documents")).status_code in (401, 403)
    no_actor = await client.post("/api/v1/admin/config/documents/intent_cues/drafts", json={"content": edited(), "note": "thử"}, headers=auth_headers)
    assert no_actor.status_code == 422  # thiếu X-Actor-Hash
    assert (await client.get("/api/v1/admin/config/documents/khong_co", headers=auth_headers)).status_code == 404


@pytest.mark.asyncio
async def test_seed_defaults_adds_one_active_row_once_and_db_then_wins(session):
    assert await docs.seed_defaults(session) == {"narrative_labels": 1}
    assert await docs.seed_defaults(session) == {"narrative_labels": 0}
    rows = (await session.execute(select(ConfigDocument).where(ConfigDocument.name == "narrative_labels"))).scalars().all()
    assert [(r.version, r.status) for r in rows] == [(1, "active")]
    assert (await docs.active_document(session, "narrative_labels"))["source"] == "db"


@pytest.mark.asyncio
async def test_seed_defaults_does_not_touch_a_document_an_admin_already_saved(session):
    session.add(ConfigDocument(name="narrative_labels", version=3, status="archived", content={"labels": ["x"]}, created_by=ACTOR))
    await session.commit()
    assert await docs.seed_defaults(session) == {"narrative_labels": 0}
    count = await session.scalar(select(func.count()).select_from(ConfigDocument).where(ConfigDocument.name == "narrative_labels"))
    assert count == 1
