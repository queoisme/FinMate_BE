"""Migration 0008: bảng ``bill_ai_instructions`` (luật phân loại món cho Gemini) và việc chuyển 4 từ khóa đồ gia dụng sang shopping.

Khác các test tích hợp còn lại (dựng schema bằng create_all), ở đây phải CHẠY ALEMBIC thật vì thứ cần kiểm là chính migration:
dòng seed và bước dữ liệu. Nên dùng một database riêng: nâng tới 0007, đặt dữ liệu cũ, rồi nâng tới head.
"""

import asyncio
import os
import subprocess
import sys
import uuid
from pathlib import Path

import pytest
from sqlalchemy import text
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import create_async_engine
from sqlalchemy.pool import NullPool

ROOT = Path(__file__).resolve().parents[2]  # thư mục ai-service (chứa alembic.ini)
DB_NAME = f"finmate_ai_mig_{uuid.uuid4().hex[:8]}"
HOUSEHOLD = ("bot giat", "nuoc rua chen", "giay ve sinh", "nuoc xa vai")
SHOPPING_PRIORITY = 7  # khác 100 mặc định và khác priority của groceries, để bắt lỗi copy nhầm


def _alembic(url: str, *args: str) -> subprocess.CompletedProcess:
    return subprocess.run(
        [sys.executable, "-m", "alembic", *args],
        cwd=ROOT, env={**os.environ, "DATABASE_URL": url}, capture_output=True, text=True,
    )


async def _admin(admin_url: str, sql: str) -> None:
    engine = create_async_engine(admin_url, isolation_level="AUTOCOMMIT", poolclass=NullPool)
    try:
        async with engine.connect() as connection:
            await connection.execute(text(sql))
    finally:
        await engine.dispose()


async def _seed_old_keywords(url: str) -> None:
    """Dữ liệu như DB thật ở 0007: 4 từ khóa nằm ở groceries; 'bot giat' ĐÃ có sẵn dòng shopping (ca xung đột unique)."""
    engine = create_async_engine(url, poolclass=NullPool)
    try:
        async with engine.begin() as connection:
            for keyword in HOUSEHOLD:
                await connection.execute(text(
                    "INSERT INTO bill_keywords (kind, category_slug, keyword, category_priority) VALUES ('product', 'groceries', :k, 3)"
                ), {"k": keyword})
            await connection.execute(text(
                "INSERT INTO bill_keywords (kind, category_slug, keyword, category_priority) VALUES ('product', 'shopping', 'bot giat', :p)"
            ), {"p": SHOPPING_PRIORITY})
            await connection.execute(text(
                "INSERT INTO bill_keywords (kind, category_slug, keyword, category_priority) VALUES ('product', 'shopping', 'ao thun', :p)"
            ), {"p": SHOPPING_PRIORITY})
    finally:
        await engine.dispose()


@pytest.fixture(scope="module")
def migrated():
    from app.core.config import settings

    base, _, _ = settings.database_url.rpartition("/")
    admin_url, url = f"{base}/postgres", f"{base}/{DB_NAME}"
    try:
        asyncio.run(_admin(admin_url, f'CREATE DATABASE "{DB_NAME}"'))
    except Exception as error:  # noqa: BLE001
        pytest.skip(f"Không kết nối được Postgres cho test tích hợp: {error}")
    try:
        for step in (("upgrade", "0007"), None, ("upgrade", "head")):
            if step is None:
                asyncio.run(_seed_old_keywords(url))
                continue
            result = _alembic(url, *step)
            assert result.returncode == 0, result.stderr
        yield url
    finally:
        asyncio.run(_admin(admin_url, f'DROP DATABASE IF EXISTS "{DB_NAME}" WITH (FORCE)'))


async def _scalars(url: str, sql: str, **params):
    engine = create_async_engine(url, poolclass=NullPool)
    try:
        async with engine.connect() as connection:
            return (await connection.execute(text(sql), params)).all()
    finally:
        await engine.dispose()


async def test_migration_seeds_one_active_instruction(migrated):
    rows = await _scalars(migrated, "SELECT instruction_text FROM bill_ai_instructions WHERE is_active")
    assert len(rows) == 1
    assert "food" in rows[0][0] and "unknown" in rows[0][0]


async def test_only_one_active_row_allowed(migrated):
    engine = create_async_engine(migrated, poolclass=NullPool)
    try:
        with pytest.raises(IntegrityError):
            async with engine.begin() as connection:
                await connection.execute(text("INSERT INTO bill_ai_instructions (instruction_text) VALUES ('x')"))
        async with engine.begin() as connection:  # dòng không active thì được thêm tùy ý
            await connection.execute(text("INSERT INTO bill_ai_instructions (instruction_text, is_active) VALUES ('x', false)"))
            await connection.execute(text("DELETE FROM bill_ai_instructions WHERE NOT is_active"))
    finally:
        await engine.dispose()


async def test_household_keywords_moved_to_shopping(migrated):
    rows = await _scalars(
        migrated, "SELECT keyword, category_slug, category_priority FROM bill_keywords WHERE kind = 'product' AND keyword = ANY(:ks)",
        ks=list(HOUSEHOLD),
    )
    assert sorted(r[0] for r in rows) == sorted(HOUSEHOLD)  # mỗi từ đúng một dòng
    assert {r[1] for r in rows} == {"shopping"}
    assert {r[2] for r in rows} == {SHOPPING_PRIORITY}  # priority của shopping, không copy 3 của groceries


async def test_migration_data_step_idempotent_and_reversible(migrated):
    assert _alembic(migrated, "downgrade", "0007").returncode == 0
    rows = await _scalars(migrated, "SELECT category_slug FROM bill_keywords WHERE keyword = ANY(:ks)", ks=list(HOUSEHOLD))
    assert {r[0] for r in rows} == {"groceries"}  # đảo chiều cả bốn, kể cả "bot giat" vốn có sẵn dòng shopping
    result = _alembic(migrated, "upgrade", "head")
    assert result.returncode == 0, result.stderr


def test_alembic_check_clean(migrated):
    result = _alembic(migrated, "check")
    assert result.returncode == 0, result.stdout + result.stderr
