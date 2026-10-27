"""Migration 0023 (gộp bảng bill_* vào bộ ml_*) chạy alembic THẬT trên dữ liệu cũ: nâng từ 0022 có dữ liệu cả hai phía, kiểm kết quả, hạ lại, nâng lại.

Như ``test_bill_ai_instructions``: dùng database riêng vì thứ cần kiểm là chính migration (chép hàng, đổi giá trị, bước lùi).
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
DB_NAME = f"finmate_ai_mig23_{uuid.uuid4().hex[:8]}"
U = "00000000-0000-0000-0000-0000000000"


def _alembic(url: str, *args: str) -> subprocess.CompletedProcess:
    return subprocess.run(
        [sys.executable, "-m", "alembic", *args], cwd=ROOT, env={**os.environ, "DATABASE_URL": url}, capture_output=True, text=True,
    )


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


LEGACY = [
    f"INSERT INTO ml_raw_samples (id, source, package_name, notification_body, received_at, content_hash) VALUES ('{U}a1','notification','com.bank','nap tien',now(),repeat('a', 64))",
    f"INSERT INTO labeled_samples (id, raw_sample_id, is_financial, transaction_type, amount_cents, category_slug, labeled_by) VALUES ('{U}b1','{U}a1',true,'credit',5000,'income','seed')",
    f"INSERT INTO ml_sample_splits (labeled_sample_id, split, split_seed) VALUES ('{U}b1','test',1)",
    f"INSERT INTO training_jobs (id, stage, status, started_at) VALUES ('{U}c1','classifier','succeeded',now())",
    f"INSERT INTO model_versions (id, stage, version, artifact_path, status, training_job_id) VALUES ('{U}d1','classifier','1.0.0','x','archived','{U}c1'),('{U}d2','classifier','1.0.1','y','active',NULL)",
    f"INSERT INTO evaluation_runs (id, model_version_id, split, sample_count, accuracy, macro_f1) VALUES ('{U}e1','{U}d2','test',10,0.9,0.8)",
    f"INSERT INTO evaluation_predictions (evaluation_run_id, labeled_sample_id, predicted_label, true_label, confidence, is_correct) VALUES ('{U}e1','{U}b1','income','income',0.95,true)",
    f"INSERT INTO bill_raw_samples (id, source, package_name, notification_title, notification_body, received_at, content_hash) VALUES ('{U}a2','bill_scan','bill_scan','Highlands','ca phe',now(),repeat('b', 64))",
    f"INSERT INTO bill_labeled_samples (id, raw_sample_id, is_financial, transaction_type, amount_cents, merchant_name, category_slug, labeled_by, is_gold) VALUES ('{U}b2','{U}a2',true,'expense',45000,'Highlands','dining','gold',true)",
    f"INSERT INTO bill_training_jobs (id, stage, status) VALUES ('{U}c2','bill_slug','finished'),('{U}c3','bill_category','succeeded')",
    f"INSERT INTO bill_model_versions (id, stage, version, artifact_path, status, training_job_id) VALUES ('{U}d3','bill_slug','v1','a','retired','{U}c2'),('{U}d4','bill_slug','v2','b','active',NULL),('{U}d5','bill_category','1','','rejected','{U}c3'),('{U}d6','bill_category','2','c','active',NULL)",
    f"INSERT INTO bill_evaluation_runs (id, model_version_id, split, sample_count, accuracy, macro_f1) VALUES ('{U}e2','{U}d6','cv',30,0.88,0.7)",
    f"INSERT INTO bill_evaluation_category_metrics (evaluation_run_id, label, \"precision\", recall, f1, support) VALUES ('{U}e2','dining',0.8,0.8,0.8,10)",
]
OLD_TABLES = ("ml_raw_samples", "labeled_samples", "bill_raw_samples", "bill_labeled_samples", "training_jobs", "bill_training_jobs",
              "model_versions", "bill_model_versions", "evaluation_runs", "bill_evaluation_runs")


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
        assert _alembic(url, "upgrade", "0022").returncode == 0
        asyncio.run(_run(url, *LEGACY))
        yield url
    finally:
        asyncio.run(_run(admin_url, f'DROP DATABASE IF EXISTS "{DB_NAME}" WITH (FORCE)'))


def counts(url: str, tables) -> dict[str, int]:
    return {t: asyncio.run(_run(url, f"SELECT count(*) FROM {t}"))[0][0] for t in tables}


def test_upgrade_merges_both_sides_and_unifies_values(database):
    before = counts(database, OLD_TABLES)
    result = _alembic(database, "upgrade", "head")
    assert result.returncode == 0, result.stderr

    after = counts(database, ("ml_raw_samples", "ml_labeled_samples", "ml_training_jobs", "ml_model_versions", "ml_evaluation_runs", "ml_evaluation_category_metrics", "ml_evaluation_predictions", "ml_models"))
    assert after["ml_raw_samples"] == before["ml_raw_samples"] + before["bill_raw_samples"]
    assert after["ml_labeled_samples"] == before["labeled_samples"] + before["bill_labeled_samples"]
    assert after["ml_training_jobs"] == before["training_jobs"] + before["bill_training_jobs"]
    assert after["ml_model_versions"] == before["model_versions"] + before["bill_model_versions"]
    assert after["ml_evaluation_runs"] == before["evaluation_runs"] + before["bill_evaluation_runs"]
    assert after["ml_evaluation_category_metrics"] == 1 and after["ml_evaluation_predictions"] == 1 and after["ml_models"] == 4

    versions = asyncio.run(_run(database, "SELECT m.code, v.version, v.status FROM ml_model_versions v JOIN ml_models m ON m.id = v.model_id ORDER BY 1, 2"))
    assert [tuple(r) for r in versions] == [
        ("bill_category", "1", "rejected"), ("bill_category", "2", "active"), ("bill_slug", "v1", "retired"), ("bill_slug", "v2", "active"),
        ("classifier", "1.0.0", "retired"), ("classifier", "1.0.1", "active"),  # archived -> retired
    ]
    jobs = asyncio.run(_run(database, "SELECT m.code, j.status, j.started_at IS NOT NULL FROM ml_training_jobs j JOIN ml_models m ON m.id = j.model_id ORDER BY 1"))
    assert [tuple(r) for r in jobs] == [("bill_category", "succeeded", True), ("bill_slug", "succeeded", True), ("classifier", "succeeded", True)]  # finished -> succeeded
    labels = asyncio.run(_run(database, "SELECT labeled_by, transaction_type, category_slug, is_gold FROM ml_labeled_samples ORDER BY 1"))
    assert [tuple(r) for r in labels] == [("gold", "debit", "dining", True), ("seed", "credit", "income", False)]  # expense -> debit
    left = asyncio.run(_run(database, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' AND table_name LIKE 'bill%' ORDER BY 1"))
    assert [r[0] for r in left] == ["bill_unknown_scans"]  # sáu bảng đã gộp biến mất; bill_user_merchant_category bị gộp ở 0029; bill_unknown_scans còn (hàng đợi duyệt)

    assert _alembic(database, "check").returncode == 0  # model khớp DB sau khi gộp


def test_downgrade_puts_every_row_back_and_upgrade_works_again(database):
    assert _alembic(database, "downgrade", "0022").returncode == 0
    assert counts(database, OLD_TABLES) == {
        "ml_raw_samples": 1, "labeled_samples": 1, "bill_raw_samples": 1, "bill_labeled_samples": 1, "training_jobs": 1, "bill_training_jobs": 2,
        "model_versions": 2, "bill_model_versions": 4, "evaluation_runs": 1, "bill_evaluation_runs": 1,
    }
    statuses = asyncio.run(_run(database, "SELECT stage, version, status FROM model_versions ORDER BY 1, 2"))
    assert [tuple(r) for r in statuses] == [("classifier", "1.0.0", "archived"), ("classifier", "1.0.1", "active")]

    result = _alembic(database, "upgrade", "head")
    assert result.returncode == 0, result.stderr
    assert _alembic(database, "check").returncode == 0
