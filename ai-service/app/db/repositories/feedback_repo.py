"""Truy cập ``user_feedback`` / ``feedback_batch_jobs``."""

import uuid
from collections.abc import Sequence

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.db.models.user_feedback import FeedbackBatchJob, UserFeedback


async def add(session: AsyncSession, feedback: UserFeedback) -> UserFeedback:
    session.add(feedback)
    await session.flush()
    return feedback


async def list_unprocessed(
    session: AsyncSession, limit: int = 500
) -> Sequence[UserFeedback]:
    result = await session.execute(
        select(UserFeedback)
        .where(UserFeedback.processed_at.is_(None))
        .order_by(UserFeedback.created_at)
        .limit(limit)
    )
    return result.scalars().all()


async def exists_for_transaction(
    session: AsyncSession, backend_transaction_id_hash: str, feedback_type: str
) -> bool:
    """Người dùng sửa đi sửa lại danh mục một giao dịch sẽ tạo nhiều dòng feedback.

    Không chặn ở tầng ghi (mỗi lần sửa là một tín hiệu thật), nhưng feedback_batch dùng
    hàm này để chỉ lấy lần sửa CUỐI làm nhãn.
    """
    result = await session.execute(
        select(UserFeedback.id)
        .where(
            UserFeedback.backend_transaction_id_hash == backend_transaction_id_hash,
            UserFeedback.feedback_type == feedback_type,
        )
        .limit(1)
    )
    return result.scalar_one_or_none() is not None


async def add_batch_job(
    session: AsyncSession, job: FeedbackBatchJob
) -> FeedbackBatchJob:
    session.add(job)
    await session.flush()
    return job


async def get_batch_job(
    session: AsyncSession, job_id: uuid.UUID
) -> FeedbackBatchJob | None:
    result = await session.execute(
        select(FeedbackBatchJob).where(FeedbackBatchJob.id == job_id)
    )
    return result.scalar_one_or_none()
