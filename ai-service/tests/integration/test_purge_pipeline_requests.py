"""Dọn ``ops_analyze_log`` quá hạn và 4 chỉ mục khóa ngoại của migration 0020, trên Postgres thật."""

import sys
import uuid
from datetime import UTC, datetime, timedelta
from pathlib import Path

from sqlalchemy import func, select, text
from sqlalchemy.ext.asyncio import async_sessionmaker

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))

from app.core.enums import PipelineResult  # noqa: E402
from app.db.models.pipeline_request import PipelineRequest  # noqa: E402
from purge_pipeline_requests import purge  # noqa: E402


def row(age_days: int) -> PipelineRequest:
    now = datetime.now(UTC)
    return PipelineRequest(
        backend_request_id=uuid.uuid4(),
        user_id_hash="a" * 64,
        package_name="com.test.bank",
        received_at=now,
        pipeline_result=PipelineResult.FINANCIAL.value,
        created_at=now - timedelta(days=age_days),
    )


async def count(session) -> int:
    return (await session.execute(select(func.count()).select_from(PipelineRequest))).scalar_one()


async def test_purge_deletes_only_rows_older_than_retention(engine, session):
    session.add_all([row(100), row(91), row(89), row(1)])
    await session.commit()

    deleted = await purge(async_sessionmaker(engine, expire_on_commit=False), days=90)

    assert deleted == 2
    assert await count(session) == 2


async def test_dry_run_counts_but_keeps_rows(engine, session):
    session.add_all([row(100), row(1)])
    await session.commit()

    would_delete = await purge(async_sessionmaker(engine, expire_on_commit=False), days=90, dry_run=True)

    assert would_delete == 1
    assert await count(session) == 2


async def test_purge_loops_over_batches(engine, session):
    session.add_all([row(200) for _ in range(5)] + [row(1)])
    await session.commit()

    deleted = await purge(async_sessionmaker(engine, expire_on_commit=False), days=90, batch_size=2)

    assert deleted == 5
    assert await count(session) == 1


async def test_foreign_key_indexes_exist(session):
    names = set(
        (await session.execute(text("SELECT indexname FROM pg_indexes WHERE schemaname = 'public'"))).scalars().all()
    )
    assert {
        "idx_pipeline_requests_raw_sample",
        "idx_model_versions_training_job",
        "idx_user_feedback_batch_job",
        "idx_evaluation_predictions_labeled_sample",
    } <= names
