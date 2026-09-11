#!/usr/bin/env python3
"""Biến ``user_feedback`` thành ``labeled_samples`` — khâu khép vòng của pipeline.

Người dùng sửa danh mục một giao dịch trên app → backend gọi ``POST /api/v1/feedback`` →
script này biến nó thành nhãn → lần ``train.py`` sau model học được đúng chỗ nó sai. Không
có bước này thì bảng ``user_feedback`` chỉ là nơi dữ liệu đi vào rồi nằm đó.

Chạy định kỳ (cron), rồi train + evaluate + promote như bình thường.

Chạy: ``python scripts/feedback_batch.py [--limit 500]``
"""

from __future__ import annotations

import argparse
import asyncio
import pathlib
import sys
from datetime import UTC, datetime

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))


from app.core.enums import FeedbackType, JobStatus, LabelSource
from app.db.models.labeled_sample import LabeledSample, SampleSplit
from app.db.models.pipeline_request import PipelineRequest
from app.db.models.raw_sample import RawSample
from app.db.models.user_feedback import FeedbackBatchJob
from app.db.repositories import feedback_repo, sample_repo
from app.db.session import AsyncSessionLocal

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from seed_dataset import split_for


async def run(limit: int) -> None:
    now = datetime.now(UTC)

    async with AsyncSessionLocal() as session:
        job = await feedback_repo.add_batch_job(
            session,
            FeedbackBatchJob(status=JobStatus.RUNNING.value, started_at=now),
        )
        await session.commit()

        pending = list(await feedback_repo.list_unprocessed(session, limit))

        # Người dùng có thể sửa đi sửa lại danh mục của cùng một giao dịch. Chỉ lần sửa
        # CUỐI mới là ý định thật; giữ cả chuỗi sẽ nhồi vào tập train hai nhãn mâu thuẫn
        # cho cùng một câu. Danh sách đã sắp theo created_at tăng dần nên ghi đè là đủ.
        latest: dict[str, object] = {}
        for row in pending:
            if row.feedback_type != FeedbackType.CATEGORY_CORRECTION.value:
                continue
            latest[row.backend_transaction_id_hash] = row

        created = 0
        for row in latest.values():
            if row.corrected_category is None or row.pipeline_request_id is None:
                continue
            if await _apply(session, row, now):
                created += 1

        for row in pending:
            row.processed_at = now
            row.batch_job_id = job.id

        job.status = JobStatus.SUCCEEDED.value
        job.finished_at = datetime.now(UTC)
        job.feedback_count = len(pending)
        job.created_labeled_samples = created
        await session.commit()

    print(f"feedback đã xử lý: {len(pending)}")
    print(f"nhãn tạo/cập nhật: {created}")
    skipped = len(pending) - created
    if skipped:
        print(
            f"bỏ qua {skipped} — không liên kết được về pipeline_requests, "
            "không phải category_correction, hoặc là bản sửa cũ của cùng giao dịch"
        )


async def _apply(session, feedback, now: datetime) -> bool:
    request = await session.get(PipelineRequest, feedback.pipeline_request_id)
    if request is None or request.raw_sample_id is None:
        return False

    raw = await session.get(RawSample, request.raw_sample_id)
    if raw is None:
        return False

    existing = await sample_repo.get_label_for_raw(session, raw.id)
    if existing is not None:
        # Người thật sửa thì thắng nhãn máy sinh — kể cả nhãn seed.
        existing.category_slug = feedback.corrected_category
        existing.labeled_by = LabelSource.USER_FEEDBACK.value
        existing.labeled_at = now
        existing.is_gold = True
        return True

    label = await sample_repo.add_label(
        session,
        LabeledSample(
            raw_sample_id=raw.id,
            is_financial=True,
            transaction_type=request.transaction_type,
            amount_cents=request.amount_cents,
            merchant_name=request.merchant_name,
            category_slug=feedback.corrected_category,
            labeled_by=LabelSource.USER_FEEDBACK.value,
            labeled_at=now,
            is_gold=True,
        ),
    )
    split, bucket = split_for(raw.content_hash)
    await sample_repo.add_split(
        session,
        SampleSplit(labeled_sample_id=label.id, split=split.value, split_seed=bucket),
    )
    return True


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--limit", type=int, default=500)
    args = parser.parse_args()
    asyncio.run(run(args.limit))


if __name__ == "__main__":
    main()
