"""Kiến thức hóa đơn trên Postgres thật: nạp thương hiệu và từ khóa từ DB, nhớ cửa hàng theo người dùng, và các ràng buộc của bảng ``bill_*``."""

import pytest
import pytest_asyncio
from sqlalchemy import text
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import async_sessionmaker

from app.bill import brands, classifier
from app.bill.classifier import classify_category_detailed
from app.bill.knowledge import DbBillKnowledge, Remembered, merchant_key
from app.services import category_resolver
from app.db.bill_views import MERCHANT_VOTES_VIEW, UNKNOWN_MERCHANTS_VIEW

USER = "a" * 64
OTHER = "b" * 64


@pytest_asyncio.fixture
async def factory(engine):
    return async_sessionmaker(engine, expire_on_commit=False)


@pytest.fixture(autouse=True)
def restore_tables():
    """Nạp từ DB sửa các bảng trong code tại chỗ: trả lại nguyên trạng để không ảnh hưởng test khác."""
    saved = (dict(brands.BRAND_CATEGORIES), dict(brands.BRAND_DISPLAY), set(brands.WHOLE_WORD_BRANDS),
             dict(classifier._CATEGORY_KEYWORDS), dict(classifier._PRODUCT_KEYWORDS))
    yield
    for target, value in zip((brands.BRAND_CATEGORIES, brands.BRAND_DISPLAY, brands.WHOLE_WORD_BRANDS,
                              classifier._CATEGORY_KEYWORDS, classifier._PRODUCT_KEYWORDS), saved):
        target.clear()
        target.update(value)
    category_resolver.invalidate()  # chỉ mục bộ phân loại chung dựng từ các bảng trên: dựng lại từ bản trong code


async def execute(session, sql: str, **params):
    await session.execute(text(sql), params)
    await session.commit()


async def add_brand(session, key, category, display, whole=False, active=True):
    await execute(session, "INSERT INTO bill_brands (brand_key, category_slug, display_name, whole_word, is_active) "
                           "VALUES (:k, :c, :d, :w, :a)", k=key, c=category, d=display, w=whole, a=active)


async def add_keyword(session, kind, category, keyword, priority=100, active=True):
    await execute(session, "INSERT INTO bill_keywords (kind, category_slug, keyword, category_priority, is_active) "
                           "VALUES (:k, :c, :w, :p, :a)", k=kind, c=category, w=keyword, p=priority, a=active)


async def test_database_brands_and_keywords_replace_the_ones_in_code(session, factory):
    await add_brand(session, "quan zzz", "dining", "Quán Zzz")
    await add_keyword(session, "merchant", "healthcare", "phong kham zzz")
    await add_keyword(session, "product", "entertainment", "ve xem zzz")

    await DbBillKnowledge(factory).refresh_if_stale()

    assert classify_category_detailed("Quán Zzz", [])[:2] == ("dining", "brand")
    assert classify_category_detailed("Phong Kham Zzz", [])[:2] == ("healthcare", "keyword")
    assert classify_category_detailed(None, ["Ve xem Zzz 90.000"])[:2] == ("entertainment", "product")
    assert classify_category_detailed("Highlands Coffee", [])[1] == "none"  # thay hẳn, không trộn với bản trong code


async def test_inactive_rows_are_ignored_and_priority_orders_the_categories(session, factory):
    await add_brand(session, "quan on", "dining", "Quán On")
    await add_brand(session, "quan off", "dining", "Quán Off", active=False)
    await add_keyword(session, "merchant", "dining", "chung zzz", priority=20)
    await add_keyword(session, "merchant", "shopping", "chung zzz", priority=10)  # số nhỏ hơn được xét trước

    await DbBillKnowledge(factory).refresh_if_stale()

    assert "quan on" in brands.BRAND_CATEGORIES and "quan off" not in brands.BRAND_CATEGORIES
    assert classify_category_detailed("Cua Hang Chung Zzz", [])[:2] == ("shopping", "keyword")


async def test_the_chat_keywords_are_not_loaded_into_the_bill_tables(session, factory):
    await add_keyword(session, "chat", "healthcare", "xyz chat only")
    await add_keyword(session, "merchant", "dining", "quan zzz ok")
    before_product = dict(classifier._PRODUCT_KEYWORDS)

    await DbBillKnowledge(factory).refresh_if_stale()

    assert "xyz chat only" not in " ".join(w for words in classifier._CATEGORY_KEYWORDS.values() for w in words)
    assert classifier._PRODUCT_KEYWORDS == before_product  # DB không có từ khóa mặt hàng thì giữ bản trong code


async def test_an_empty_database_keeps_the_tables_in_code(factory):
    before = dict(brands.BRAND_CATEGORIES)

    await DbBillKnowledge(factory).refresh_if_stale()

    assert dict(brands.BRAND_CATEGORIES) == before
    assert classify_category_detailed("Highlands Coffee", [])[:2] == ("dining", "brand")


async def test_knowledge_is_refreshed_only_after_the_ttl(session, factory):
    now = [1000.0]
    knowledge = DbBillKnowledge(factory, ttl=60.0, clock=lambda: now[0])
    await add_brand(session, "quan mot", "dining", "Quán Một")
    await knowledge.refresh_if_stale()
    await add_brand(session, "quan hai", "dining", "Quán Hai")

    now[0] += 30
    await knowledge.refresh_if_stale()
    assert "quan hai" not in brands.BRAND_CATEGORIES  # chưa hết hạn: không đọc lại DB

    now[0] += 31
    await knowledge.refresh_if_stale()
    assert "quan hai" in brands.BRAND_CATEGORIES


async def test_the_memory_is_per_user_and_matches_any_spelling_of_the_shop(session, factory):
    await execute(session, "INSERT INTO bill_user_merchant_category (user_id_hash, merchant_key, category_slug) VALUES (:u, :k, 'entertainment')",
                  u=USER, k=merchant_key("Highlands Coffee"))
    knowledge = DbBillKnowledge(factory)

    assert await knowledge.remembered(USER, "HIGHLANDS   Coffee") == Remembered("entertainment", backend_slug=False)  # bảng cũ: 9 nhóm
    assert await knowledge.remembered(OTHER, "Highlands Coffee") is None
    assert await knowledge.remembered(USER, "Phúc Long") is None
    assert await knowledge.remembered(USER, None) is None
    assert await knowledge.remembered(USER, "   ") is None


async def test_a_broken_database_never_breaks_classification(engine):
    class Broken:
        def __call__(self):
            raise ConnectionError("db down")

    knowledge = DbBillKnowledge(Broken())  # type: ignore[arg-type]
    before = dict(brands.BRAND_CATEGORIES)

    await knowledge.refresh_if_stale()

    assert dict(brands.BRAND_CATEGORIES) == before
    assert await knowledge.remembered(USER, "Highlands Coffee") is None


async def test_the_tables_reject_data_the_code_would_never_write(session):
    raw = "INSERT INTO bill_raw_samples (source, package_name, notification_body, received_at, content_hash) VALUES (:s, 'bill_scan', 'x', now(), :h)"
    await execute(session, raw, s="bill_scan", h="h1")
    with pytest.raises(IntegrityError):
        await execute(session, raw, s="notification", h="h2")  # nguồn khác bill_scan
    await session.rollback()

    label = ("INSERT INTO bill_labeled_samples (raw_sample_id, is_financial, category_slug, labeled_by) "
             "SELECT id, true, :c, :b FROM bill_raw_samples WHERE content_hash = 'h1'")
    with pytest.raises(IntegrityError):
        await execute(session, label, c="food", b="user")  # slug BE không thuộc 9 nhóm của bill
    await session.rollback()
    with pytest.raises(IntegrityError):
        await execute(session, label, c="dining", b="seed")  # người gắn nhãn lạ
    await session.rollback()
    await execute(session, label, c="dining", b="user")


async def test_deleting_a_raw_sample_deletes_its_label(session):
    await execute(session, "INSERT INTO bill_raw_samples (source, package_name, notification_body, received_at, content_hash) "
                           "VALUES ('bill_scan', 'bill_scan', 'x', now(), 'hc')")
    await execute(session, "INSERT INTO bill_labeled_samples (raw_sample_id, is_financial, category_slug, labeled_by) "
                           "SELECT id, true, 'dining', 'user' FROM bill_raw_samples WHERE content_hash = 'hc'")

    await execute(session, "DELETE FROM bill_raw_samples WHERE content_hash = 'hc'")

    assert (await session.execute(text("SELECT count(*) FROM bill_labeled_samples"))).scalar_one() == 0


async def test_the_review_views_group_unknown_shops_and_votes(session):
    await execute(session, UNKNOWN_MERCHANTS_VIEW)  # create_all không tạo view
    await execute(session, MERCHANT_VOTES_VIEW)
    await execute(session, "INSERT INTO bill_unknown_scans (user_id_hash, merchant_read, merchant_key, predicted_category, lines, content_hash, seen_count) "
                           "VALUES (:u, 'Quán Lạ', 'quan la', 'dining', 'x', 'c1', 3)", u=USER)
    await execute(session, "INSERT INTO bill_user_merchant_category (user_id_hash, merchant_key, display_name, category_slug) VALUES "
                           "(:a, 'quan la', 'Quán Lạ', 'dining'), (:b, 'quan la', 'Quán Lạ', 'dining')", a=USER, b=OTHER)

    unknown = (await session.execute(text("SELECT merchant_key, scans, users FROM bill_unknown_merchants"))).all()
    votes = (await session.execute(text("SELECT merchant_key, category_slug, users FROM bill_merchant_votes"))).all()

    assert [tuple(r) for r in unknown] == [("quan la", 3, 1)]
    assert [tuple(r) for r in votes] == [("quan la", "dining", 2)]


async def test_learn_remembers_the_backend_slug_the_user_chose(factory):
    knowledge = DbBillKnowledge(factory)

    assert await knowledge.learn(USER, "  Highlands   COFFEE ", "entertainment") is True

    assert await knowledge.remembered(USER, "highlands coffee") == Remembered("entertainment", backend_slug=True)
    assert await knowledge.remembered(OTHER, "highlands coffee") is None  # theo từng người dùng


async def test_learning_again_replaces_the_choice_instead_of_adding_a_row(session, factory):
    knowledge = DbBillKnowledge(factory)
    await knowledge.learn(USER, "Highlands Coffee", "entertainment")

    await knowledge.learn(USER, "highlands coffee", "food")

    assert await knowledge.remembered(USER, "Highlands Coffee") == Remembered("food", backend_slug=True)
    assert (await session.execute(text("SELECT count(*) FROM bill_user_merchant_choice"))).scalar_one() == 1


async def test_a_choice_made_when_saving_beats_the_old_nine_group_memory(session, factory):
    await execute(session, "INSERT INTO bill_user_merchant_category (user_id_hash, merchant_key, category_slug) VALUES (:u, 'highlands coffee', 'transport')", u=USER)
    knowledge = DbBillKnowledge(factory)
    assert await knowledge.remembered(USER, "Highlands Coffee") == Remembered("transport", backend_slug=False)

    await knowledge.learn(USER, "Highlands Coffee", "health")

    assert await knowledge.remembered(USER, "Highlands Coffee") == Remembered("health", backend_slug=True)


async def test_a_name_with_no_key_is_not_learned(factory):
    assert await DbBillKnowledge(factory).learn(USER, "    ", "food") is False


async def test_the_database_only_accepts_backend_expense_slugs_as_choices(session, factory):
    insert = "INSERT INTO bill_user_merchant_choice (user_id_hash, merchant_key, category_slug) VALUES (:u, :k, :c)"
    for bad in ("income", "dining", "nope"):  # thu nhập, nhóm cũ của bill, slug lạ
        with pytest.raises(IntegrityError):
            await execute(session, insert, u=USER, k=f"k-{bad}", c=bad)
        await session.rollback()
    await execute(session, insert, u=USER, k="k-ok", c="food")
    with pytest.raises(IntegrityError):  # một người, một cửa hàng, một dòng
        await execute(session, insert, u=USER, k="k-ok", c="bills")


async def test_database_keywords_reach_the_resolver_after_refresh(session, factory):
    category_resolver.invalidate()
    assert category_resolver.resolve("zxqv thing").slug is None
    await add_keyword(session, "product", "shopping", "zxqv thing")

    await DbBillKnowledge(factory).refresh_if_stale()

    assert category_resolver.resolve("zxqv thing").slug == "shopping"
