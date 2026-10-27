"""Migration 0030: bảng ``kb_method_prompts`` (chỉ dẫn prompt của bước chọn cách quản lý tài chính) và bộ nạp ``method_picker.get_method_prompt``.

Như ``test_bill_ai_instructions``, chạy ALEMBIC thật trên một database riêng vì thứ cần kiểm là chính migration: dòng seed, các ràng buộc, bước lùi.
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
from sqlalchemy.ext.asyncio import async_sessionmaker, create_async_engine
from sqlalchemy.pool import NullPool

from app.services import method_picker
from app.services.method_prompt_seed import METHOD_PROMPT_TEXT, METHOD_PROMPT_VERSION

ROOT = Path(__file__).resolve().parents[2]  # thư mục ai-service (chứa alembic.ini)
DB_NAME = f"finmate_ai_mig_{uuid.uuid4().hex[:8]}"


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
        result = _alembic(url, "upgrade", "head")
        assert result.returncode == 0, result.stderr
        yield url
    finally:
        asyncio.run(_admin(admin_url, f'DROP DATABASE IF EXISTS "{DB_NAME}" WITH (FORCE)'))


async def _run(url: str, sql: str, **params):
    engine = create_async_engine(url, poolclass=NullPool)
    try:
        async with engine.begin() as connection:
            result = await connection.execute(text(sql), params)
            return result.all() if result.returns_rows else None
    finally:
        await engine.dispose()


async def test_migration_seeds_one_active_prompt(migrated):
    rows = await _run(migrated, "SELECT prompt_version, instruction_text FROM kb_method_prompts WHERE is_active")
    assert rows == [(METHOD_PROMPT_VERSION, METHOD_PROMPT_TEXT)]


async def test_only_one_active_row_allowed_and_old_versions_are_kept(migrated):
    with pytest.raises(IntegrityError):
        await _run(migrated, "INSERT INTO kb_method_prompts (prompt_version, instruction_text) VALUES ('v2', 'x')")  # is_active mặc định true
    await _run(migrated, "INSERT INTO kb_method_prompts (prompt_version, instruction_text, is_active) VALUES ('v2', 'x', false)")
    # đổi chỉ dẫn = chuyển cờ trong MỘT giao dịch; dòng cũ còn nguyên để truy ngược gợi ý cũ
    await _run(migrated, "UPDATE kb_method_prompts SET is_active = (prompt_version = 'v2')")
    assert await _run(migrated, "SELECT prompt_version FROM kb_method_prompts WHERE is_active") == [("v2",)]
    assert (await _run(migrated, "SELECT count(*) FROM kb_method_prompts"))[0][0] == 2
    await _run(migrated, "DELETE FROM kb_method_prompts WHERE prompt_version = 'v2'")
    await _run(migrated, "UPDATE kb_method_prompts SET is_active = true WHERE prompt_version = :v", v=METHOD_PROMPT_VERSION)


@pytest.mark.parametrize("version", [METHOD_PROMPT_VERSION, "co khoang trang", "", "v" * 51, "tiếng-việt"])
async def test_version_must_be_unique_and_well_formed(migrated, version):
    with pytest.raises(Exception) as error:  # 'v' * 51 là DataError (varchar(50)); còn lại IntegrityError
        await _run(migrated, "INSERT INTO kb_method_prompts (prompt_version, instruction_text, is_active) VALUES (:v, 'x', false)", v=version)
    assert "kb_method_prompts" in str(error.value) or "value too long" in str(error.value)


async def test_instruction_text_must_not_be_empty(migrated):
    with pytest.raises(IntegrityError):
        await _run(migrated, "INSERT INTO kb_method_prompts (prompt_version, instruction_text, is_active) VALUES ('v-empty', '', false)")


@pytest.fixture
def loader(migrated, monkeypatch):
    """``get_method_prompt`` trỏ vào DB thử, với cache sạch."""
    engine = create_async_engine(migrated, poolclass=NullPool)
    monkeypatch.setattr(method_picker, "AsyncSessionLocal", async_sessionmaker(engine, expire_on_commit=False))
    monkeypatch.setattr(method_picker, "_prompt", None)
    yield
    asyncio.run(engine.dispose())


async def test_loader_reads_version_and_text_from_the_same_active_row(migrated, loader):
    row = await method_picker.get_method_prompt()
    assert (row.version, row.text) == (METHOD_PROMPT_VERSION, METHOD_PROMPT_TEXT)


async def test_loader_caches_then_picks_up_a_new_active_version(migrated, loader, monkeypatch):
    first = await method_picker.get_method_prompt()
    await _run(migrated, "INSERT INTO kb_method_prompts (prompt_version, instruction_text, is_active) VALUES ('v-new', 'CHI-DAN-MOI', false)")
    await _run(migrated, "UPDATE kb_method_prompts SET is_active = (prompt_version = 'v-new')")
    try:
        assert await method_picker.get_method_prompt() == first  # còn trong 60 giây: dùng cache
        monkeypatch.setattr(method_picker, "_prompt", None)  # hết hạn
        row = await method_picker.get_method_prompt()
        assert (row.version, row.text) == ("v-new", "CHI-DAN-MOI")  # phiên bản và nội dung luôn đi cùng nhau
        await _run(migrated, "UPDATE kb_method_prompts SET is_active = false")
        monkeypatch.setattr(method_picker, "_prompt", None)
        assert await method_picker.get_method_prompt() is None  # không dòng nào active → route trả prompt_missing
    finally:
        await _run(migrated, "DELETE FROM kb_method_prompts WHERE prompt_version = 'v-new'")
        await _run(migrated, "UPDATE kb_method_prompts SET is_active = true WHERE prompt_version = :v", v=METHOD_PROMPT_VERSION)


async def test_loader_keeps_the_last_good_prompt_when_the_db_fails(migrated, loader, monkeypatch):
    good = await method_picker.get_method_prompt()

    class Broken:
        def __call__(self):
            raise ConnectionError("db down")

    monkeypatch.setattr(method_picker, "AsyncSessionLocal", Broken())
    monkeypatch.setattr(method_picker, "_prompt", (-1e9, good))  # đã hết hạn
    assert await method_picker.get_method_prompt() == good
    monkeypatch.setattr(method_picker, "_prompt", None)
    assert await method_picker.get_method_prompt() is None  # chưa từng nạp được: None, không ném lỗi


def test_downgrade_drops_the_table_and_upgrade_restores_the_seed(migrated):
    assert _alembic(migrated, "downgrade", "0029").returncode == 0
    assert asyncio.run(_run(migrated, "SELECT to_regclass('kb_method_prompts')")) == [(None,)]
    result = _alembic(migrated, "upgrade", "head")
    assert result.returncode == 0, result.stderr
    assert asyncio.run(_run(migrated, "SELECT prompt_version FROM kb_method_prompts WHERE is_active")) == [(METHOD_PROMPT_VERSION,)]


def test_alembic_check_clean(migrated):
    result = _alembic(migrated, "check")
    assert result.returncode == 0, result.stdout + result.stderr
