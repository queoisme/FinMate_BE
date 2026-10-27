"""``kb_categories`` và các khóa ngoại ``category_slug`` (migration 0022) trên Postgres thật."""

import uuid
from datetime import UTC, datetime

import pytest
from sqlalchemy import text
from sqlalchemy.exc import IntegrityError

from app.core.enums import PipelineResult
from app.db.models.pipeline_request import PipelineRequest

INSERT_BRAND = "INSERT INTO kb_brands (brand_key, category_slug, display_name) VALUES (:k, :c, 'x')"


async def rejected(session, sql: str, **params) -> bool:
    try:
        await session.execute(text(sql), params)
        await session.commit()
        return False
    except IntegrityError:
        await session.rollback()
        return True


async def test_legacy_groups_are_alias_rows_pointing_at_backend_slugs(session):
    rows = (await session.execute(text("SELECT slug, replaced_by FROM kb_categories WHERE NOT is_active ORDER BY slug"))).all()
    assert [tuple(r) for r in rows] == [("dining", "food"), ("groceries", "food"), ("healthcare", "health"), ("utilities", "bills")]


async def test_brands_accept_legacy_groups_and_reject_unknown_slugs(session):
    for slug in ("dining", "groceries", "food"):
        assert not await rejected(session, INSERT_BRAND, k=f"brand {slug}", c=slug)
    assert await rejected(session, INSERT_BRAND, k="brand zzz", c="zzz")


async def test_backend_only_tables_still_reject_alias_rows(session):
    # khóa ngoại một mình sẽ cho 'dining' lọt vào; CHECK liệt kê slug backend vẫn chặn
    assert await rejected(session, "INSERT INTO kb_item_categories (item_key, category_slug) VALUES ('khan uot', 'dining')")
    assert await rejected(session, "INSERT INTO user_merchant_memory (user_id_hash, merchant_key, category_slug) VALUES ('u', 'cua hang', 'groceries')")
    assert not await rejected(session, "INSERT INTO kb_item_categories (item_key, category_slug) VALUES ('khan uot', 'food')")


async def test_analyze_log_and_feedback_reject_unknown_slugs(session):
    session.add(PipelineRequest(
        backend_request_id=uuid.uuid4(), user_id_hash="a" * 64, package_name="com.test.bank",
        received_at=datetime.now(UTC), pipeline_result=PipelineResult.FINANCIAL.value, category_slug="zzz",
    ))
    with pytest.raises(IntegrityError):
        await session.commit()
    await session.rollback()

    insert = "INSERT INTO ml_user_feedback (backend_transaction_id_hash, user_id_hash, package_name, corrected_category, feedback_type) VALUES ('h', 'u', 'p', :c, 'category_correction')"
    assert await rejected(session, insert, c="zzz")


async def test_category_cannot_be_deleted_while_in_use_but_slug_change_cascades(session):
    await session.execute(text("INSERT INTO kb_categories (slug, label) VALUES ('pets', 'Thú cưng')"))
    await session.execute(text(INSERT_BRAND), {"k": "petmart", "c": "pets"})
    await session.commit()

    assert await rejected(session, "DELETE FROM kb_categories WHERE slug = 'pets'")

    await session.execute(text("UPDATE kb_categories SET slug = 'animals' WHERE slug = 'pets'"))
    await session.commit()
    assert (await session.execute(text("SELECT category_slug FROM kb_brands WHERE brand_key = 'petmart'"))).scalar_one() == "animals"


async def test_category_table_constraints(session):
    assert await rejected(session, "INSERT INTO kb_categories (slug, label) VALUES ('Bad-Slug', 'x')")  # chỉ a-z và gạch dưới
    assert await rejected(session, "INSERT INTO kb_categories (slug, label, taxonomy_version) VALUES ('old_one', 'x', 0)")
    assert await rejected(session, "UPDATE kb_categories SET replaced_by = 'food' WHERE slug = 'food'")  # không tự thay chính nó
    assert await rejected(session, "INSERT INTO kb_categories (slug, label, replaced_by) VALUES ('ghost', 'x', 'nowhere')")
