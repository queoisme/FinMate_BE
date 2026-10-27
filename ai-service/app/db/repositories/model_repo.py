"""Truy cập ``ml_model_versions`` / ``ml_training_jobs`` — registry model của pipeline thông báo (``ModelStage``).

Bảng dùng chung với model hóa đơn (``bill_slug``, ``bill_category``); ``list_active`` chỉ trả model của pipeline thông báo để vòng nạp model không gặp mã lạ."""

import uuid
from collections.abc import Sequence

from sqlalchemy import select, update
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.enums import ModelStage, ModelStatus
from app.db.models.ml_model import MlModel
from app.db.models.model_version import ModelVersion, TrainingJob

_PIPELINE_STAGES = [stage.value for stage in ModelStage]


async def model_id_for(session: AsyncSession, code: str) -> uuid.UUID:
    """Id của model trong ``ml_models`` theo ``code`` (= ``stage`` cũ). Model chưa đăng ký thì báo lỗi rõ, không tự tạo: đăng ký là quyết định của người."""
    model_id = (await session.execute(select(MlModel.id).where(MlModel.code == code))).scalar_one_or_none()
    if model_id is None:
        raise ValueError(f"model {code!r} chưa có trong ml_models")
    return model_id


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
        select(ModelVersion).where(
            ModelVersion.status == ModelStatus.ACTIVE.value,
            ModelVersion.stage.in_(_PIPELINE_STAGES),
        )
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
    ``retired``.

    Phải hạ TRƯỚC rồi mới nâng: partial unique index ``uq_ml_model_versions_active`` chặn hai
    dòng cùng model cùng active, làm ngược thứ tự sẽ vi phạm index ngay giữa transaction.
    """
    target = await get_by_id(session, model_version_id)
    if target is None:
        raise ValueError(f"model_version {model_version_id} không tồn tại")

    await session.execute(
        update(ModelVersion)
        .where(
            ModelVersion.model_id == target.model_id,
            ModelVersion.status == ModelStatus.ACTIVE.value,
            ModelVersion.id != target.id,
        )
        .values(status=ModelStatus.RETIRED.value)
    )
    await session.flush()
    target.status = ModelStatus.ACTIVE.value
    await session.flush()


async def add_training_job(session: AsyncSession, job: TrainingJob) -> TrainingJob:
    session.add(job)
    await session.flush()
    return job
