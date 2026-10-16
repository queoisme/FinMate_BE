"""Quản trị luật phân loại (thương hiệu, từ khóa, bộ nhớ tên món, chỉ dẫn Gemini) trên Postgres thật: mỗi thay đổi có nhật ký và hoàn tác được."""

import uuid

import pytest
import pytest_asyncio
from sqlalchemy import delete, select

from app.bill import rules_admin as rules
from app.bill.rules_admin import RuleError
from app.db.models.bill import BillAiInstruction, BillBrand, BillItemCache, BillKeyword, BillRuleAudit

ACTOR = "a" * 64


@pytest_asyncio.fixture(autouse=True)
async def no_instructions(session):
    """Bảng chỉ dẫn không nằm trong danh sách dọn chung của conftest và test khác có thể để lại một dòng đang bật."""
    await session.execute(delete(BillAiInstruction))
    await session.commit()


async def audit_rows(session, resource=None):
    query = select(BillRuleAudit).order_by(BillRuleAudit.created_at, BillRuleAudit.id)
    if resource:
        query = query.where(BillRuleAudit.resource == resource)
    return (await session.execute(query)).scalars().all()


# ---------------------------------------------------------------- thương hiệu

async def test_upsert_brand_creates_then_updates_and_audits(session):
    created = await rules.upsert_brand(session, "Highlands Coffee", category_slug="dining", display_name="Highlands", whole_word=False, actor=ACTOR)
    assert created["brand_key"] == "highlands coffee" and created["source"] == "admin" and created["is_active"] is True
    updated = await rules.upsert_brand(session, "highlands coffee", category_slug="shopping", display_name="Highlands", whole_word=True, actor=ACTOR, reason="thử")
    assert updated["category_slug"] == "shopping" and updated["whole_word"] is True
    rows = await audit_rows(session, "brand")
    assert [r.action for r in rows] == ["create", "update"]
    assert rows[0].before is None and rows[0].after["category_slug"] == "dining"
    assert rows[1].before["category_slug"] == "dining" and rows[1].after["category_slug"] == "shopping" and rows[1].reason == "thử"
    assert all(r.actor_hash == ACTOR for r in rows)


@pytest.mark.parametrize("key,category", [("", "dining"), ("   ", "dining"), ("x store", "income"), ("x store", "family"), ("x store", "nope")])
async def test_brand_rejects_invalid_input(session, key, category):
    with pytest.raises(RuleError) as error:
        await rules.upsert_brand(session, key, category_slug=category, display_name="X", whole_word=False, actor=ACTOR)
    assert error.value.status == 422
    assert not (await session.execute(select(BillBrand))).scalars().all() and not await audit_rows(session)


async def test_set_brand_active_toggles_and_unknown_is_404(session):
    await rules.upsert_brand(session, "Zeta Mart", category_slug="groceries", display_name="Zeta", whole_word=False, actor=ACTOR)
    off = await rules.set_brand_active(session, "zeta mart", False, actor=ACTOR)
    assert off["is_active"] is False
    assert (await audit_rows(session, "brand"))[-1].action == "deactivate"
    assert (await rules.set_brand_active(session, "zeta mart", True, actor=ACTOR))["is_active"] is True
    with pytest.raises(RuleError) as error:
        await rules.set_brand_active(session, "khong co", False, actor=ACTOR)
    assert error.value.status == 404


async def test_list_brands_filters_and_pages(session):
    for name, category in [("Alpha Shop", "shopping"), ("Alpine Cafe", "dining"), ("Beta Cafe", "dining")]:
        await rules.upsert_brand(session, name, category_slug=category, display_name=name, whole_word=False, actor=ACTOR)
    await rules.set_brand_active(session, "beta cafe", False, actor=ACTOR)
    items, total = await rules.list_brands(session, q="alp")
    assert total == 2 and {i["brand_key"] for i in items} == {"alpha shop", "alpine cafe"}
    items, total = await rules.list_brands(session, category_slug="dining", active=True)
    assert total == 1 and items[0]["brand_key"] == "alpine cafe"
    items, total = await rules.list_brands(session, limit=1, offset=1)
    assert total == 3 and len(items) == 1


# ------------------------------------------------------------------ từ khóa

async def test_create_keyword_normalises_and_audits(session):
    created = await rules.create_keyword(session, kind="product", category_slug="housing", keyword="Cây Lau Nhà", actor=ACTOR)
    assert created["keyword"] == "cay lau nha" and created["source"] == "admin" and created["is_active"] is True and created["warnings"] == []
    row = (await audit_rows(session, "keyword"))[0]
    assert row.action == "create" and row.resource_key == created["id"] and row.after["keyword"] == "cay lau nha"


async def test_create_keyword_warns_about_conflicts_but_saves(session):
    await rules.create_keyword(session, kind="product", category_slug="dining", keyword="lau", actor=ACTOR)
    second = await rules.create_keyword(session, kind="product", category_slug="housing", keyword="lau", actor=ACTOR)
    assert {"same_keyword_other_category", "short_keyword"} <= {w["code"] for w in second["warnings"]}


@pytest.mark.parametrize("kind,category,keyword", [
    ("chat", "dining", "pho"), ("product", "income", "pho"), ("product", "family", "pho"),
    ("product", "dining", ""), ("product", "dining", "   "), ("product", "dining", "12345"), ("product", "dining", "x" * 61),
])
async def test_keyword_rejects_invalid_input(session, kind, category, keyword):
    with pytest.raises(RuleError) as error:
        await rules.create_keyword(session, kind=kind, category_slug=category, keyword=keyword, actor=ACTOR)
    assert error.value.status == 422
    assert not (await session.execute(select(BillKeyword))).scalars().all()


async def test_duplicate_active_keyword_is_409_and_inactive_one_is_reactivated(session):
    first = await rules.create_keyword(session, kind="product", category_slug="dining", keyword="bun bo", actor=ACTOR)
    with pytest.raises(RuleError) as error:
        await rules.create_keyword(session, kind="product", category_slug="dining", keyword="Bún Bò", actor=ACTOR)
    assert error.value.status == 409
    await rules.set_keyword_active(session, uuid.UUID(first["id"]), False, actor=ACTOR)
    again = await rules.create_keyword(session, kind="product", category_slug="dining", keyword="bun bo", actor=ACTOR)
    assert again["id"] == first["id"] and again["is_active"] is True


async def test_set_keyword_active_and_unknown_id_is_404(session):
    created = await rules.create_keyword(session, kind="merchant", category_slug="dining", keyword="quan nhau", actor=ACTOR)
    assert (await rules.set_keyword_active(session, uuid.UUID(created["id"]), False, actor=ACTOR))["is_active"] is False
    with pytest.raises(RuleError) as error:
        await rules.set_keyword_active(session, uuid.uuid4(), False, actor=ACTOR)
    assert error.value.status == 404


async def test_list_keywords_filters(session):
    await rules.create_keyword(session, kind="product", category_slug="dining", keyword="pho bo", actor=ACTOR)
    await rules.create_keyword(session, kind="merchant", category_slug="dining", keyword="quan pho", actor=ACTOR)
    await rules.create_keyword(session, kind="product", category_slug="shopping", keyword="ao thun", actor=ACTOR)
    items, total = await rules.list_keywords(session, kind="product")
    assert total == 2
    items, total = await rules.list_keywords(session, q="pho")
    assert total == 2
    items, total = await rules.list_keywords(session, category_slug="shopping")
    assert total == 1 and items[0]["keyword"] == "ao thun"


# ------------------------------------------------------- bộ nhớ tên món

async def test_put_item_cache_creates_updates_and_audits(session):
    created = await rules.put_item_cache(session, "Ổ Cắm Điện 60.000", "shopping", actor=ACTOR)
    assert created["item_key"] == "o cam dien" and created["category_slug"] == "shopping"
    updated = await rules.put_item_cache(session, "o cam dien", "housing", actor=ACTOR)
    assert updated["category_slug"] == "housing"
    rows = await audit_rows(session, "item_cache")
    assert [r.action for r in rows] == ["create", "update"] and rows[1].before["category_slug"] == "shopping"


@pytest.mark.parametrize("name,slug", [("SP002993", "shopping"), ("o cam dien", "income"), ("o cam dien", "nope"), ("", "shopping")])
async def test_item_cache_rejects_invalid_input(session, name, slug):
    with pytest.raises(RuleError) as error:
        await rules.put_item_cache(session, name, slug, actor=ACTOR)
    assert error.value.status == 422
    assert not (await session.execute(select(BillItemCache))).scalars().all()


async def test_delete_item_cache_removes_and_audits(session):
    await rules.put_item_cache(session, "bim cho be", "family", actor=ACTOR)
    await rules.delete_item_cache(session, "Bỉm cho bé", actor=ACTOR)
    assert not (await session.execute(select(BillItemCache))).scalars().all()
    last = (await audit_rows(session, "item_cache"))[-1]
    assert last.action == "delete" and last.before["category_slug"] == "family" and last.after is None
    with pytest.raises(RuleError) as error:
        await rules.delete_item_cache(session, "bim cho be", actor=ACTOR)
    assert error.value.status == 404


async def test_list_item_cache_search(session):
    await rules.put_item_cache(session, "bim cho be", "family", actor=ACTOR)
    await rules.put_item_cache(session, "o cam dien", "shopping", actor=ACTOR)
    items, total = await rules.list_item_cache(session, q="bim")
    assert total == 1 and items[0]["item_key"] == "bim cho be"


# ---------------------------------------------------------- chỉ dẫn Gemini

async def active_instructions(session):
    return (await session.execute(select(BillAiInstruction).where(BillAiInstruction.is_active))).scalars().all()


async def test_create_instruction_activates_it_and_retires_the_previous_one(session):
    first = await rules.create_instruction(session, "Chỉ dẫn 1", actor=ACTOR)
    second = await rules.create_instruction(session, "Chỉ dẫn 2", actor=ACTOR, reason="đổi luật")
    active = await active_instructions(session)
    assert len(active) == 1 and str(active[0].id) == second["id"] and first["id"] != second["id"]
    listed = await rules.list_instructions(session)
    assert [i["instruction_text"] for i in listed] == ["Chỉ dẫn 2", "Chỉ dẫn 1"] and [i["is_active"] for i in listed] == [True, False]
    assert [r.action for r in await audit_rows(session, "instruction")] == ["create", "create"]


@pytest.mark.parametrize("text", ["", "   \n", "x" * 20_001])
async def test_instruction_rejects_blank_or_oversized(session, text):
    with pytest.raises(RuleError) as error:
        await rules.create_instruction(session, text, actor=ACTOR)
    assert error.value.status == 422 and not await active_instructions(session)


async def test_activate_an_older_instruction_and_unknown_is_404(session):
    first = await rules.create_instruction(session, "Chỉ dẫn 1", actor=ACTOR)
    await rules.create_instruction(session, "Chỉ dẫn 2", actor=ACTOR)
    back = await rules.activate_instruction(session, uuid.UUID(first["id"]), actor=ACTOR)
    assert back["is_active"] is True and [str(i.id) for i in await active_instructions(session)] == [first["id"]]
    assert (await audit_rows(session, "instruction"))[-1].action == "activate"
    with pytest.raises(RuleError) as error:
        await rules.activate_instruction(session, uuid.uuid4(), actor=ACTOR)
    assert error.value.status == 404


# ------------------------------------------------------- lịch sử và hoàn tác

async def test_history_is_newest_first_and_filterable(session):
    await rules.upsert_brand(session, "Alpha Shop", category_slug="shopping", display_name="Alpha", whole_word=False, actor=ACTOR)
    await rules.put_item_cache(session, "o cam dien", "shopping", actor=ACTOR)
    items, total = await rules.list_history(session)
    assert total == 2 and [i["resource"] for i in items] == ["item_cache", "brand"]
    items, total = await rules.list_history(session, resource="brand")
    assert total == 1 and items[0]["resource_key"] == "alpha shop"


async def test_revert_a_created_brand_deactivates_it(session):
    await rules.upsert_brand(session, "Alpha Shop", category_slug="shopping", display_name="Alpha", whole_word=False, actor=ACTOR)
    entry = (await rules.list_history(session))[0][0]
    result = await rules.revert(session, uuid.UUID(entry["id"]), actor=ACTOR)
    assert result["resource"] == "brand" and result["state"]["is_active"] is False
    assert (await audit_rows(session, "brand"))[-1].action == "revert"


async def test_revert_an_updated_brand_restores_the_previous_values(session):
    await rules.upsert_brand(session, "Alpha Shop", category_slug="shopping", display_name="Alpha", whole_word=False, actor=ACTOR)
    await rules.upsert_brand(session, "Alpha Shop", category_slug="dining", display_name="Alpha Cafe", whole_word=True, actor=ACTOR)
    entry = (await rules.list_history(session))[0][0]
    assert entry["action"] == "update"
    result = await rules.revert(session, uuid.UUID(entry["id"]), actor=ACTOR)
    assert result["state"]["category_slug"] == "shopping" and result["state"]["display_name"] == "Alpha" and result["state"]["whole_word"] is False


async def test_revert_keyword_create_and_deactivate(session):
    created = await rules.create_keyword(session, kind="product", category_slug="dining", keyword="pho bo", actor=ACTOR)
    await rules.revert(session, uuid.UUID((await rules.list_history(session))[0][0]["id"]), actor=ACTOR)  # hoàn tác lần tạo
    assert (await session.execute(select(BillKeyword).where(BillKeyword.id == uuid.UUID(created["id"])))).scalar_one().is_active is False
    await rules.set_keyword_active(session, uuid.UUID(created["id"]), True, actor=ACTOR)
    await rules.set_keyword_active(session, uuid.UUID(created["id"]), False, actor=ACTOR)
    await rules.revert(session, uuid.UUID((await rules.list_history(session))[0][0]["id"]), actor=ACTOR)  # hoàn tác lần tắt
    assert (await session.execute(select(BillKeyword).where(BillKeyword.id == uuid.UUID(created["id"])))).scalar_one().is_active is True


async def test_revert_item_cache_create_update_and_delete(session):
    await rules.put_item_cache(session, "o cam dien", "shopping", actor=ACTOR)
    await rules.put_item_cache(session, "o cam dien", "housing", actor=ACTOR)
    await rules.revert(session, uuid.UUID((await rules.list_history(session, resource="item_cache"))[0][0]["id"]), actor=ACTOR)
    assert (await session.execute(select(BillItemCache))).scalar_one().category_slug == "shopping"
    await rules.delete_item_cache(session, "o cam dien", actor=ACTOR)
    await rules.revert(session, uuid.UUID((await rules.list_history(session, resource="item_cache"))[0][0]["id"]), actor=ACTOR)
    assert (await session.execute(select(BillItemCache))).scalar_one().category_slug == "shopping"
    first_create = (await rules.list_history(session, resource="item_cache"))[0][-1]
    await rules.revert(session, uuid.UUID(first_create["id"]), actor=ACTOR)  # hoàn tác lần tạo đầu tiên: xóa dòng
    assert not (await session.execute(select(BillItemCache))).scalars().all()


async def test_revert_unknown_entry_is_404_and_instruction_entries_use_activate(session):
    with pytest.raises(RuleError) as error:
        await rules.revert(session, uuid.uuid4(), actor=ACTOR)
    assert error.value.status == 404
    await rules.create_instruction(session, "Chỉ dẫn 1", actor=ACTOR)
    with pytest.raises(RuleError) as error:
        await rules.revert(session, uuid.UUID((await rules.list_history(session))[0][0]["id"]), actor=ACTOR)
    assert error.value.status == 422
