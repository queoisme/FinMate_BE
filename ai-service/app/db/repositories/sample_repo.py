"""Truy cập ``raw_samples`` / ``labeled_samples`` / ``sample_splits``."""

import uuid
from collections.abc import Sequence

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.enums import SplitName
from app.db.models.labeled_sample import LabeledSample, SampleSplit
from app.db.models.raw_sample import RawSample


async def get_raw_by_content_hash(
    session: AsyncSession, content_hash: str
) -> RawSample | None:
    result = await session.execute(
        select(RawSample).where(RawSample.content_hash == content_hash)
    )
    return result.scalar_one_or_none()


async def add_raw_if_absent(session: AsyncSession, sample: RawSample) -> RawSample:
    """Idempotent theo ``content_hash``.

    Cùng một thông báo vào kho hai lần sẽ làm lệch phân bố tập train — và với retry của
    ``RetryFailedNotificationJob`` phía backend thì chuyện đó xảy ra thường xuyên.
    """
    existing = await get_raw_by_content_hash(session, sample.content_hash)
    if existing is not None:
        return existing
    session.add(sample)
    await session.flush()
    return sample


async def get_label_for_raw(
    session: AsyncSession, raw_sample_id: uuid.UUID
) -> LabeledSample | None:
    result = await session.execute(
        select(LabeledSample).where(LabeledSample.raw_sample_id == raw_sample_id)
    )
    return result.scalar_one_or_none()


async def add_label(session: AsyncSession, label: LabeledSample) -> LabeledSample:
    session.add(label)
    await session.flush()
    return label


async def get_split(
    session: AsyncSession, labeled_sample_id: uuid.UUID
) -> SampleSplit | None:
    result = await session.execute(
        select(SampleSplit).where(SampleSplit.labeled_sample_id == labeled_sample_id)
    )
    return result.scalar_one_or_none()


async def add_split(session: AsyncSession, split: SampleSplit) -> SampleSplit:
    session.add(split)
    await session.flush()
    return split


async def load_split(
    session: AsyncSession, split: SplitName
) -> Sequence[tuple[RawSample, LabeledSample]]:
    """Mẫu thô kèm nhãn của một tập — đầu vào của train.py và evaluate.py."""
    stmt = (
        select(RawSample, LabeledSample)
        .join(LabeledSample, LabeledSample.raw_sample_id == RawSample.id)
        .join(SampleSplit, SampleSplit.labeled_sample_id == LabeledSample.id)
        .where(SampleSplit.split == split.value)
        .order_by(RawSample.content_hash)
    )
    result = await session.execute(stmt)
    return [(row[0], row[1]) for row in result.all()]
