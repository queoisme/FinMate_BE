"""Migration 0039: bảng ``user_item_memory`` (bộ nhớ danh mục theo món của từng người dùng).

Chạy ALEMBIC thật trên một database riêng vì thứ cần kiểm là chính migration: bảng và khóa chính, bốn ràng buộc CHECK, bước lùi và nâng lại.
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
USER = "a" * 64
INSERT = "INSERT INTO user_item_memory (user_id_hash, item_key, category_id, source) VALUES (:u, :k, :c, :s)"


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


async def _run(url: str, sql: str, **params):
    engine = create_async_engine(url, poolclass=NullPool)
    try:
        async with engine.begin() as connection:
            result = await connection.execute(text(sql), params)
            return result.all() if result.returns_rows else None
    finally:
        await engine.dispose()


@pytest.fixture(scope="module")
def database():
    from app.core.config import settings

    base, _, _ = settings.database_url.rpartition("/")
    admin_url, url = f"{base}/postgres", f"{base}/{DB_NAME}"
    try:
        asyncio.run(_admin(admin_url, f'CREATE DATABASE "{DB_NAME}"'))
    except Exception as error:  # noqa: BLE001
        pytest.skip(f"Không kết nối được Postgres cho test tích hợp: {error}")
    try:
        result = _alembic(url, "upgrade", "0039")
        assert result.returncode == 0, result.stderr
        yield url
    finally:
        asyncio.run(_admin(admin_url, f'DROP DATABASE IF EXISTS "{DB_NAME}" WITH (FORCE)'))


async def test_upgrade_creates_the_table_with_its_primary_key(database):
    await _run(database, INSERT, u=USER, k="ca phe sua", c=uuid.uuid4(), s="accepted")
    with pytest.raises(IntegrityError) as error:
        await _run(database, INSERT, u=USER, k="ca phe sua", c=uuid.uuid4(), s="corrected")  # trùng (user, món)
    assert "pk_user_item_memory" in str(error.value)
    await _run(database, INSERT, u="b" * 64, k="ca phe sua", c=uuid.uuid4(), s="corrected")  # người khác, cùng món: được
    await _run(database, "DELETE FROM user_item_memory")


@pytest.mark.parametrize(
    ("constraint", "user", "key", "source"),
    [
        ("chk_user_item_memory_source", USER, "ca phe sua", "other"),
        ("chk_user_item_memory_key_lower", USER, "Ca Phe Sua", "accepted"),
        ("chk_user_item_memory_key_len", USER, "ab", "accepted"),
        ("chk_user_item_memory_key_len", USER, "a" * 61, "accepted"),
        ("chk_user_item_memory_user_hash", "A" * 64, "ca phe sua", "accepted"),
        ("chk_user_item_memory_user_hash", "a" * 63, "ca phe sua", "accepted"),
    ],
)
async def test_check_constraints_reject_bad_rows(database, constraint, user, key, source):
    with pytest.raises(IntegrityError) as error:
        await _run(database, INSERT, u=user, k=key, c=uuid.uuid4(), s=source)
    assert constraint in str(error.value)


async def test_boundary_key_lengths_are_accepted(database):
    await _run(database, INSERT, u=USER, k="abc", c=uuid.uuid4(), s="accepted")
    await _run(database, INSERT, u=USER, k="a" * 60, c=uuid.uuid4(), s="corrected")
    await _run(database, "DELETE FROM user_item_memory")


async def test_downgrade_drops_the_table_and_upgrade_works_again(database):
    assert _alembic(database, "downgrade", "0038").returncode == 0
    assert (await _run(database, "SELECT to_regclass('user_item_memory')"))[0][0] is None

    assert _alembic(database, "upgrade", "0039").returncode == 0
    assert (await _run(database, "SELECT to_regclass('user_item_memory')"))[0][0] is not None
