"""Trigger kiểm LOẠI danh mục (migration 0026) ở từng cột: slug chi, thu và dòng bí danh vào đúng bảng, sai bảng, trên Postgres thật."""

import uuid

import pytest
from sqlalchemy import text
from sqlalchemy.exc import IntegrityError

H = "1" * 64


async def attempt(session, sql: str, **params) -> bool:
    """True nếu bị DB từ chối."""
    try:
        await session.execute(text(sql), params)
        await session.commit()
        return False
    except IntegrityError:
        await session.rollback()
        return True


ITEM = "INSERT INTO kb_item_categories (item_key, category_slug) VALUES (:k, :c)"
MEMORY = "INSERT INTO user_merchant_memory (user_id_hash, merchant_key, category_slug) VALUES (repeat('1', 64), :k, :c)"
TRAINING = "INSERT INTO ml_training_samples (scan_id, user_id_hash, lines, final_slug) VALUES (:id, repeat('1', 64), 'x', :c)"
VOTE = "INSERT INTO ml_item_feedback (item_key, user_id_hash, category_slug) VALUES (:k, :u, :c)"
FEEDBACK = "INSERT INTO ml_user_feedback (backend_transaction_id_hash, user_id_hash, package_name, predicted_category, corrected_category, feedback_type) VALUES (repeat('2', 64), repeat('1', 64), 'p', :p, :c, 'category_correction')"


@pytest.mark.parametrize("slug, accepted", [("food", True), ("income", False), ("dining", False), ("zzz", False)])
async def test_spend_only_tables_accept_only_expense_categories(session, slug, accepted):
    assert (not await attempt(session, ITEM, k="khan uot", c=slug)) is accepted
    assert (not await attempt(session, MEMORY, k="cua hang", c=slug)) is accepted
    assert (not await attempt(session, TRAINING, id=uuid.uuid4(), c=slug)) is accepted
    assert (not await attempt(session, VOTE, k="khan uot", u=H, c=slug)) is accepted


@pytest.mark.parametrize("slug, accepted", [("food", True), ("income", True), ("dining", False), ("zzz", False)])
async def test_feedback_accepts_income_but_never_legacy_aliases(session, slug, accepted):
    assert (not await attempt(session, FEEDBACK, p=None, c=slug)) is accepted
    assert (not await attempt(session, FEEDBACK, p=slug, c=None)) is accepted


async def test_a_null_category_is_always_fine(session):
    assert not await attempt(session, FEEDBACK, p=None, c=None)
    assert not await attempt(session, TRAINING, id=uuid.uuid4(), c=None)


async def test_updating_the_slug_is_checked_too(session):
    assert not await attempt(session, ITEM, k="khan uot", c="food")

    assert await attempt(session, "UPDATE kb_item_categories SET category_slug = 'income' WHERE item_key = 'khan uot'")
    assert await attempt(session, "UPDATE kb_item_categories SET category_slug = 'dining' WHERE item_key = 'khan uot'")
    assert not await attempt(session, "UPDATE kb_item_categories SET category_slug = 'shopping' WHERE item_key = 'khan uot'")


async def test_tables_without_a_guard_still_take_legacy_aliases(session):
    # kb_brands và ml_labeled_samples chỉ có khóa ngoại: nhóm cũ của bill vẫn dùng được ở đó (phương án B)
    assert not await attempt(session, "INSERT INTO kb_brands (brand_key, category_slug, display_name) VALUES ('highlands', 'dining', 'H')")


async def test_a_new_category_needs_no_check_change(session):
    await session.execute(text("INSERT INTO kb_categories (slug, label, kind) VALUES ('pets', 'Thú cưng', 'expense')"))
    await session.commit()
    try:
        assert not await attempt(session, ITEM, k="hat cho meo", c="pets")
        assert not await attempt(session, MEMORY, k="petmart", c="pets")
        assert not await attempt(session, FEEDBACK, p=None, c="pets")
    finally:
        for table in ("kb_item_categories", "user_merchant_memory", "ml_user_feedback"):
            await session.execute(text(f"DELETE FROM {table}"))
        await session.execute(text("DELETE FROM kb_categories WHERE slug = 'pets'"))
        await session.commit()
