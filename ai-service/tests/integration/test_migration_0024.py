"""Migration 0024 (sự kiện nhãn, phiếu theo món, view đồng thuận) chạy alembic THẬT: nhãn có sẵn được chép thành sự kiện đầu tiên, bước lùi dọn sạch, nâng lại được.

Cùng kiểu với ``test_migration_0023``: database riêng, nâng tới 0023, đặt dữ liệu cũ, nâng tới head.
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
DB_NAME = f"finmate_ai_mig24_{uuid.uuid4().hex[:8]}"
# (labeled_by, category, source mong đợi của sự kiện đầu tiên)
LABELS = [
    ("seed", "food", "import"), ("imported", "dining", "import"), ("user_feedback", "shopping", "feedback"), ("human", "health", "user"),
    ("user", "dining", "user"), ("gold", "food", "gold"), ("synthetic_ocr", "other", "system"),
]


def _alembic(url: str, *args: str) -> subprocess.CompletedProcess:
    return subprocess.run([sys.executable, "-m", "alembic", *args], cwd=ROOT, env={**os.environ, "DATABASE_URL": url}, capture_output=True, text=True)


async def _run(url: str, *statements: str):
    engine = create_async_engine(url, isolation_level="AUTOCOMMIT", poolclass=NullPool)
    try:
        async with engine.connect() as connection:
            result = None
            for statement in statements:
                result = await connection.execute(text(statement))
            return result.all() if result is not None and result.returns_rows else None
    finally:
        await engine.dispose()


def _legacy() -> list[str]:
    statements = []
    for i, (by, category, _) in enumerate(LABELS):
        raw, label = f"00000000-0000-0000-0000-00000000a0{i:02d}", f"00000000-0000-0000-0000-00000000b0{i:02d}"
        statements.append(f"INSERT INTO ml_raw_samples (id, source, package_name, notification_body, received_at, content_hash) VALUES ('{raw}', 'notification', 'p', 'x', now(), md5('h{i}') || md5('x{i}'))")
        statements.append(f"INSERT INTO ml_labeled_samples (id, raw_sample_id, is_financial, category_slug, labeled_by, labeled_at) VALUES ('{label}', '{raw}', true, '{category}', '{by}', now() - interval '{i + 1} days')")
    return statements


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
        assert _alembic(url, "upgrade", "0023").returncode == 0
        asyncio.run(_run(url, *_legacy()))
        yield url
    finally:
        asyncio.run(_run(admin_url, f'DROP DATABASE IF EXISTS "{DB_NAME}" WITH (FORCE)'))


def test_existing_labels_become_one_first_event_each(database):
    result = _alembic(database, "upgrade", "head")
    assert result.returncode == 0, result.stderr

    rows = asyncio.run(_run(database, "SELECT l.labeled_by, e.category_slug, e.source, e.created_at = l.labeled_at, e.feedback_id IS NULL "
                                      "FROM ml_label_events e JOIN ml_labeled_samples l ON l.id = e.labeled_sample_id ORDER BY l.labeled_at DESC"))
    assert [(r[0], r[1], r[2]) for r in rows] == [(by, category, source) for by, category, source in LABELS]
    assert all(r[3] and r[4] for r in rows)  # đúng thời điểm đặt nhãn; dữ liệu cũ không biết phản hồi nào

    assert asyncio.run(_run(database, "SELECT count(*) FROM v_merchant_consensus"))[0][0] == 0  # view tạo được trên dữ liệu có sẵn
    assert _alembic(database, "check").returncode == 0


def test_downgrade_removes_the_new_objects_and_upgrade_works_again(database):
    assert _alembic(database, "downgrade", "0023").returncode == 0
    left = asyncio.run(_run(database, "SELECT to_regclass('ml_label_events'), to_regclass('ml_item_feedback'), to_regclass('v_merchant_consensus'), to_regclass('v_item_consensus')"))[0]
    assert tuple(left) == (None, None, None, None)
    assert asyncio.run(_run(database, "SELECT count(*) FROM pg_proc WHERE proname = 'ml_label_events_append_only'"))[0][0] == 0

    result = _alembic(database, "upgrade", "head")
    assert result.returncode == 0, result.stderr
    assert asyncio.run(_run(database, "SELECT count(*) FROM ml_label_events"))[0][0] == len(LABELS)
    assert _alembic(database, "check").returncode == 0
