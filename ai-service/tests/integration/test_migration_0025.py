"""Migration 0025 (định dạng theo nhà cung cấp) chạy alembic THẬT: pattern cũ được gom thành định dạng v1 kèm trường và mẫu, hạ lại, nâng lại.

Cùng kiểu với ``test_migration_0023`` / ``0024``: database riêng, nâng tới 0024, đặt pattern cũ (dữ liệu mặc định + một package cũ đã tắt hết), nâng tới head.
"""

import asyncio
import json
import os
import subprocess
import sys
import uuid
from pathlib import Path

import pytest
from sqlalchemy import text
from sqlalchemy.ext.asyncio import create_async_engine
from sqlalchemy.pool import NullPool

from app.db.seed_patterns import SEED_PATH

ROOT = Path(__file__).resolve().parents[2]
DB_NAME = f"finmate_ai_mig25_{uuid.uuid4().hex[:8]}"
OLD_PACKAGE = "com.old.bank"


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


def _seed_rows() -> list[dict]:
    return json.loads(SEED_PATH.read_text(encoding="utf-8"))


def _seed_packages() -> set[str]:
    return {row["package_name"] for row in _seed_rows()}


def _legacy_rows() -> list[dict]:
    rows = [{**row, "is_active": True} for row in _seed_rows()]
    rows.append({"provider_key": "old_bank", "package_name": OLD_PACKAGE, "pattern_name": "old_v1", "regex": r"(?P<amount>\d+)d", "priority": 10,
                 "transaction_type": "debit", "sample_text": "khong khop mau nay", "is_active": False})
    return rows


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
        assert _alembic(url, "upgrade", "0024").returncode == 0
        for row in _legacy_rows():
            asyncio.run(_run(url, "INSERT INTO kb_provider_patterns (provider_key, package_name, pattern_name, regex, transaction_type, priority, sample_text, is_active) "
                                  "VALUES (:provider_key, :package_name, :pattern_name, :regex, :transaction_type, :priority, :sample_text, :is_active)",
                             {k: row.get(k) for k in ("provider_key", "package_name", "pattern_name", "regex", "transaction_type", "priority", "sample_text", "is_active")}))
        yield url
    finally:
        asyncio.run(_run(admin_url, f'DROP DATABASE IF EXISTS "{DB_NAME}" WITH (FORCE)'))


def test_old_patterns_become_version_one_formats_with_fields_and_samples(database):
    result = _alembic(database, "upgrade", "head")
    assert result.returncode == 0, result.stderr

    formats = asyncio.run(_run(database, "SELECT package_name, version, status FROM kb_provider_formats ORDER BY package_name"))
    assert len(formats) == len(_seed_packages()) + 1 and {f[1] for f in formats} == {1}
    assert {f[0]: f[2] for f in formats}[OLD_PACKAGE] == "archived"  # package mà mọi pattern đều đã tắt: không bật lại ngầm
    assert {f[2] for f in formats if f[0] != OLD_PACKAGE} == {"active"}

    orphans = asyncio.run(_run(database, "SELECT count(*) FROM kb_provider_patterns WHERE format_id IS NULL"))[0][0]
    assert orphans == 0

    fields = asyncio.run(_run(database, "SELECT f.field_key, f.maps_to, f.is_required FROM kb_provider_format_fields f JOIN kb_provider_formats x ON x.id = f.format_id WHERE x.package_name = 'com.mbmobile'"))
    assert ("amount", "amount_cents", True) in {tuple(r) for r in fields}

    samples = asyncio.run(_run(database, "SELECT count(*) FROM kb_provider_format_samples"))[0][0]
    assert samples == len(_seed_rows())  # mọi pattern mặc định mang mẫu khớp; mẫu "khong khop" của package cũ bị bỏ qua
    expected = asyncio.run(_run(database, "SELECT expected_fields FROM kb_provider_format_samples WHERE sample_text LIKE '%HIGHLANDS%' LIMIT 1"))[0][0]
    assert expected["amount_cents"] == 75_000 and expected["transaction_type"] == "debit"

    assert _alembic(database, "check").returncode == 0


def test_downgrade_removes_formats_and_upgrade_rebuilds_them(database):
    assert _alembic(database, "downgrade", "0024").returncode == 0
    gone = asyncio.run(_run(database, "SELECT to_regclass('kb_provider_formats'), to_regclass('kb_provider_format_fields'), to_regclass('kb_provider_format_samples')"))[0]
    assert tuple(gone) == (None, None, None)
    assert asyncio.run(_run(database, "SELECT count(*) FROM kb_provider_patterns"))[0][0] == len(_seed_rows()) + 1  # pattern cũ còn nguyên

    result = _alembic(database, "upgrade", "head")
    assert result.returncode == 0, result.stderr
    assert asyncio.run(_run(database, "SELECT count(*) FROM kb_provider_formats"))[0][0] == len(_seed_packages()) + 1
    assert _alembic(database, "check").returncode == 0
