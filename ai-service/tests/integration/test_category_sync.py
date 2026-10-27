"""``PUT /api/v1/admin/categories/sync``: backend đẩy danh mục mới sang AI Service, và danh mục đó dùng được ngay (không migration, không deploy)."""

import uuid

import pytest
from sqlalchemy import text

from app.core.anonymizer import sha256_hex
from app.services import category_registry

ACTOR = sha256_hex("admin-1")
TEST_SLUGS = ("pets", "bonus", "gifts")


def body(*categories, **extra) -> dict:
    return {"categories": list(categories), **extra}


def pets(**overrides) -> dict:
    return {"slug": "pets", "label": "Thú cưng", "kind": "expense", "is_active": True, **overrides}


@pytest.fixture(autouse=True)
async def clean_added_categories(engine):
    """Danh mục thêm trong test nằm ở bảng không bị TRUNCATE giữa các test: dọn dòng phụ thuộc rồi dòng danh mục, và đưa bộ nhớ danh mục về mốc."""
    category_registry.reset()
    yield
    async with engine.begin() as connection:
        for table in ("kb_item_categories", "user_merchant_memory", "ml_user_feedback"):
            await connection.execute(text(f"DELETE FROM {table}"))
        await connection.execute(text("TRUNCATE kb_audit_log"))  # nhật ký chỉ-ghi-thêm: không DELETE được, chỉ TRUNCATE
        await connection.execute(text("DELETE FROM kb_categories WHERE slug = ANY(:slugs)"), {"slugs": list(TEST_SLUGS)})
    category_registry.reset()


async def put(client, auth_headers, payload, actor=ACTOR):
    headers = {**auth_headers, **({"X-Actor-Hash": actor} if actor else {})}
    return await client.put("/api/v1/admin/categories/sync", json=payload, headers=headers)


async def test_a_new_category_is_created_listed_audited_and_known_at_once(client, auth_headers, session):
    response = await put(client, auth_headers, body(pets(), {"slug": "bonus", "label": "Thưởng", "kind": "income"}))

    assert response.status_code == 200 and response.json() == {"created": 2, "updated": 0, "unchanged": 0}
    listed = {i["slug"]: i for i in (await client.get("/api/v1/admin/categories", headers=auth_headers)).json()["items"]}
    assert (listed["pets"]["kind"], listed["bonus"]["kind"], listed["dining"]["kind"]) == ("expense", "income", "legacy")  # dòng bí danh vẫn thấy được nhưng không đồng bộ được
    assert category_registry.is_spend("pets") and category_registry.is_known("bonus") and not category_registry.is_spend("bonus")
    audit = (await session.execute(text("SELECT resource_key, action, actor_hash FROM kb_audit_log WHERE resource = 'category' ORDER BY resource_key"))).all()
    assert [tuple(r) for r in audit] == [("bonus", "create", ACTOR), ("pets", "create", ACTOR)]


async def test_a_synced_category_works_in_the_tables_that_used_to_need_a_new_check(client, auth_headers, session):
    await put(client, auth_headers, body(pets()))

    await session.execute(text("INSERT INTO kb_item_categories (item_key, category_slug) VALUES ('hat cho meo', 'pets')"))
    await session.execute(text("INSERT INTO user_merchant_memory (user_id_hash, merchant_key, category_slug) VALUES (repeat('1', 64), 'petmart', 'pets')"))
    await session.commit()


async def test_income_category_is_refused_by_spend_only_tables(client, auth_headers, session):
    await put(client, auth_headers, body({"slug": "bonus", "label": "Thưởng", "kind": "income"}))

    with pytest.raises(Exception, match="loại income"):
        await session.execute(text("INSERT INTO kb_item_categories (item_key, category_slug) VALUES ('thuong tet', 'bonus')"))
    await session.rollback()


async def test_feedback_for_a_synced_category_is_kept_instead_of_dropped(client, auth_headers, session):
    def feedback():
        return {"backend_transaction_id_hash": sha256_hex(str(uuid.uuid4())), "user_id_hash": sha256_hex("u1"), "pipeline_request_id": None,
                "package_name": "com.mbmobile", "predicted_category": "food", "corrected_category": "pets", "feedback_type": "category_correction"}

    assert (await client.post("/api/v1/feedback", json=feedback(), headers=auth_headers)).status_code == 200
    await put(client, auth_headers, body(pets()))
    assert (await client.post("/api/v1/feedback", json=feedback(), headers=auth_headers)).status_code == 200

    rows = (await session.execute(text("SELECT corrected_category FROM ml_user_feedback ORDER BY created_at, id"))).scalars().all()
    assert rows == [None, "pets"]  # trước khi đồng bộ danh mục bị bỏ về NULL (như trước đây); sau đó được giữ lại làm nhãn


async def test_sync_is_idempotent_and_records_only_real_changes(client, auth_headers, session):
    await put(client, auth_headers, body(pets()))

    again = await put(client, auth_headers, body(pets()))
    renamed = await put(client, auth_headers, body(pets(label="Thú cưng & vật nuôi")))
    off = await put(client, auth_headers, body(pets(label="Thú cưng & vật nuôi", is_active=False)))
    on = await put(client, auth_headers, body(pets(label="Thú cưng & vật nuôi")))

    assert [r.json() for r in (again, renamed, off, on)] == [
        {"created": 0, "updated": 0, "unchanged": 1}, {"created": 0, "updated": 1, "unchanged": 0}, {"created": 0, "updated": 1, "unchanged": 0}, {"created": 0, "updated": 1, "unchanged": 0},
    ]
    actions = (await session.execute(text("SELECT action FROM kb_audit_log WHERE resource = 'category' ORDER BY created_at, id"))).scalars().all()
    assert actions == ["create", "update", "deactivate", "activate"]
    assert category_registry.is_known("pets")  # cuối cùng đã bật lại


async def test_a_switched_off_category_stops_being_accepted_for_new_data(client, auth_headers):
    await put(client, auth_headers, body(pets()))
    assert category_registry.is_known("pets")

    await put(client, auth_headers, body(pets(is_active=False)))

    assert not category_registry.is_known("pets")


async def test_conflicts_are_refused_with_409_and_change_nothing(client, auth_headers, session):
    await put(client, auth_headers, body(pets()))

    legacy = await put(client, auth_headers, body({"slug": "dining", "label": "x", "kind": "expense"}))
    kind_change = await put(client, auth_headers, body(pets(kind="income")))

    assert (legacy.status_code, kind_change.status_code) == (409, 409)
    assert legacy.json()["detail"]["code"] == "category_conflict"
    kind = (await session.execute(text("SELECT kind FROM kb_categories WHERE slug = 'pets'"))).scalar_one()
    assert kind == "expense"


async def test_bad_requests_are_422_and_missing_credentials_are_refused(client, auth_headers):
    assert (await put(client, auth_headers, body(pets(slug="Pets")))).status_code == 422  # slug chỉ a-z và gạch dưới
    assert (await put(client, auth_headers, body(pets(kind="legacy")))).status_code == 422  # legacy không đặt được qua API
    assert (await put(client, auth_headers, body(pets(), pets()))).status_code == 422  # slug lặp
    assert (await put(client, auth_headers, {"categories": []})).status_code == 422
    assert (await put(client, auth_headers, body(pets()), actor=None)).status_code == 422  # thiếu người thực hiện
    assert (await put(client, {}, body(pets()))).status_code in (401, 403)  # thiếu khóa nội bộ
