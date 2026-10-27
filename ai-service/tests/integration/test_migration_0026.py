"""Migration 0026 (loại danh mục + trigger thay CHECK) chạy alembic THẬT: gán ``kind`` cho dữ liệu có sẵn, bỏ bảy CHECK, hạ lại và nâng lại.

Cùng kiểu với ``test_migration_0023`` đến ``0025``: database riêng, nâng tới 0025, đặt dữ liệu cũ, nâng tới head.
"""

import asyncio
import os
import subprocess
import sys
import uuid
from pathlib import Path

import pytest
from sqlalchemy import text
from sqlalchemy.ext.asyncio import create_async_engine
from sqlalchemy.pool import NullPool

ROOT = Path(__file__).resolve().parents[2]
DB_NAME = f"finmate_ai_mig26_{uuid.uuid4().hex[:8]}"
OLD_CHECKS = ("chk_bill_item_cache_slug", "chk_bill_user_merchant_choice_slug", "chk_bill_training_samples_final_slug", "chk_pipeline_requests_category_slug",
              "chk_user_feedback_predicted_category", "chk_user_feedback_corrected_category", "chk_ml_item_feedback_slug")


def _alembic(url: str, *args: str) -> subprocess.CompletedProcess:
    return subprocess.run([sys.executable, "-m", "alembic", *args], cwd=ROOT, env={**os.environ, "DATABASE_URL": url}, capture_output=True, text=True)


async def _run(url: str, statement: str, params: dict | None = None):
    engine = create_async_engine(url, isolation_level="AUTOCOMMIT", poolclass=NullPool)
    try:
        async with engine.connect() as connection:
            result = await connection.execute(text(statement), params or {})
            return result.all() if result.returns_rows else None
    finally:
        await engine.dispose()


def rows(url: str, statement: str):
    return [tuple(r) for r in asyncio.run(_run(url, statement))]


@pytest.fixture(scope="module")
def database():
    from app.core.config import settings

    base, _, _ = settings.database_url.rpartition("/")
    admin_url, url = f"{base}/postgres", f"{base}/{DB_NAME}"
    try:
        asyncio.run(_run(admin_url, f'CREATE DATABASE "{DB_NAME}"'))
    except Exception as error:  # noqa: BLE001
        pytest.skip(f"Không kết nối được Postgres cho test tích hợp: {error}")
    try:
        assert _alembic(url, "upgrade", "0025").returncode == 0
        asyncio.run(_run(url, "INSERT INTO kb_item_categories (item_key, category_slug) VALUES ('khan uot', 'food')"))
        asyncio.run(_run(url, "INSERT INTO kb_brands (brand_key, category_slug, display_name) VALUES ('highlands', 'dining', 'H')"))
        yield url
    finally:
        asyncio.run(_run(admin_url, f'DROP DATABASE IF EXISTS "{DB_NAME}" WITH (FORCE)'))


def test_kinds_are_assigned_checks_are_replaced_and_data_survives(database):
    result = _alembic(database, "upgrade", "head")
    assert result.returncode == 0, result.stderr

    kinds = dict(rows(database, "SELECT slug, kind FROM kb_categories"))
    assert kinds["income"] == "income" and kinds["food"] == "expense" and kinds["other"] == "expense"
    assert {slug for slug, kind in kinds.items() if kind == "legacy"} == {"dining", "groceries", "utilities", "healthcare"}  # đúng các dòng bí danh

    left = {r[0] for r in rows(database, "SELECT conname FROM pg_constraint WHERE conname LIKE 'chk\\_%'")}
    assert not left & set(OLD_CHECKS)  # bảy CHECK liệt kê slug đã biến mất
    triggers = {r[0] for r in rows(database, "SELECT tgname FROM pg_trigger WHERE tgname LIKE 'trg\\_%\\_kind'")}
    assert len(triggers) == 7
    assert rows(database, "SELECT count(*) FROM kb_item_categories") == [(1,)] and rows(database, "SELECT count(*) FROM kb_brands") == [(1,)]

    assert _alembic(database, "check").returncode == 0


def test_the_trigger_is_live_after_the_upgrade(database):
    with pytest.raises(Exception, match="loại legacy"):
        asyncio.run(_run(database, "INSERT INTO kb_item_categories (item_key, category_slug) VALUES ('bot giat', 'dining')"))
    asyncio.run(_run(database, "INSERT INTO kb_categories (slug, label, kind) VALUES ('pets', 'Thú cưng', 'expense')"))
    asyncio.run(_run(database, "INSERT INTO kb_item_categories (item_key, category_slug) VALUES ('hat cho meo', 'pets')"))  # danh mục mới: không cần sửa CHECK


def test_downgrade_restores_the_checks_and_stops_when_new_categories_exist(database):
    failed = _alembic(database, "downgrade", "0025")
    assert failed.returncode != 0  # đã có dữ liệu dùng danh mục 'pets' ngoài danh sách cũ: bước lùi dừng thay vì nuốt dữ liệu
    assert rows(database, "SELECT to_regclass('kb_categories')") != [(None,)]

    asyncio.run(_run(database, "DELETE FROM kb_item_categories WHERE category_slug = 'pets'"))
    asyncio.run(_run(database, "DELETE FROM kb_categories WHERE slug = 'pets'"))
    ok = _alembic(database, "downgrade", "0025")
    assert ok.returncode == 0, ok.stderr
    assert {r[0] for r in rows(database, "SELECT conname FROM pg_constraint WHERE conname LIKE 'chk\\_%'")} >= set(OLD_CHECKS)
    assert rows(database, "SELECT count(*) FROM pg_proc WHERE proname = 'kb_require_category_kind'") == [(0,)]

    again = _alembic(database, "upgrade", "head")
    assert again.returncode == 0, again.stderr
    assert _alembic(database, "check").returncode == 0
