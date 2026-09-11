"""Truy cập ``model_versions`` / ``training_jobs`` — registry model."""

import uuid
from collections.abc import Sequence

from sqlalchemy import select, update
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.enums import ModelStage, ModelStatus
from app.db.models.model_version import ModelVersion, TrainingJob


async def get_active(session: AsyncSession, stage: ModelStage) -> ModelVersion | None:
    result = await session.execute(
        select(ModelVersion).where(
            ModelVersion.stage == stage.value,
            ModelVersion.status == ModelStatus.ACTIVE.value,
        )
    )
    return result.scalar_one_or_none()


async def list_active(session: AsyncSession) -> Sequence[ModelVersion]:
    result = await session.execute(
        select(ModelVersion).where(ModelVersion.status == ModelStatus.ACTIVE.value)
    )
    return result.scalars().all()


async def get_by_id(
    session: AsyncSession, model_version_id: uuid.UUID
) -> ModelVersion | None:
    result = await session.execute(
        select(ModelVersion).where(ModelVersion.id == model_version_id)
    )
    return result.scalar_one_or_none()


async def get_by_stage_version(
    session: AsyncSession, stage: ModelStage, version: str
) -> ModelVersion | None:
    result = await session.execute(
        select(ModelVersion).where(
            ModelVersion.stage == stage.value, ModelVersion.version == version
        )
    )
    return result.scalar_one_or_none()


async def add_version(session: AsyncSession, version: ModelVersion) -> ModelVersion:
    session.add(version)
    await session.flush()
    return version


async def promote(session: AsyncSession, model_version_id: uuid.UUID) -> None:
    """Đẩy một version lên ``active`` và hạ version đang active của cùng stage xuống
    ``archived``.

    Phải hạ TRƯỚC rồi mới nâng: partial unique index ``uq_model_versions_active`` chặn hai
    dòng cùng stage cùng active, làm ngược thứ tự sẽ vi phạm index ngay giữa transaction.
    """
    target = await get_by_id(session, model_version_id)
    if target is None:
        raise ValueError(f"model_version {model_version_id} không tồn tại")

    await session.execute(
        update(ModelVersion)
        .where(
            ModelVersion.stage == target.stage,
            ModelVersion.status == ModelStatus.ACTIVE.value,
            ModelVersion.id != target.id,
        )
        .values(status=ModelStatus.ARCHIVED.value)
    )
    await session.flush()
    target.status = ModelStatus.ACTIVE.value
    await session.flush()


async def add_training_job(session: AsyncSession, job: TrainingJob) -> TrainingJob:
    session.add(job)
    await session.flush()
    return job
