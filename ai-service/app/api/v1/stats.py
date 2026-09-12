"""``GET /api/v1/stats`` — tình trạng model và dataset, cho màn hình quản trị của backend.

Bổ sung vào contract ở ARCHITECTURE.md §3.3 (route thứ ba, đã được duyệt ở Phase 8).

Endpoint này KHÔNG trả bất cứ thứ gì gắn với một người dùng cụ thể: toàn bộ là số đếm và
điểm số ở mức hệ thống. Nó cũng không trả được "AI đoán đúng bao nhiêu phần trăm ngoài đời"
— AI DB chỉ thấy dự đoán của chính mình, không thấy người dùng đã xác nhận hay sửa gì; con
số đó backend tự tính từ ``ai_results`` × ``transactions``.
"""

from __future__ import annotations

from typing import Annotated

from fastapi import APIRouter, Depends
from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.deps import get_db, verify_internal_api_key
from app.core.enums import ModelStage, ModelStatus
from app.db.models.evaluation import EvaluationRun
from app.db.models.labeled_sample import LabeledSample, SampleSplit
from app.db.models.model_version import ModelVersion, TrainingJob
from app.db.models.raw_sample import RawSample
from app.db.models.user_feedback import UserFeedback
from app.schemas.response import (
    ModelStatusResult,
    StatsResponse,
    TrainingJobResult,
)

router = APIRouter(dependencies=[Depends(verify_internal_api_key)])


@router.get("/stats", response_model=StatsResponse)
async def stats(session: Annotated[AsyncSession, Depends(get_db)]) -> StatsResponse:
    return StatsResponse(
        models=await _models(session),
        raw_sample_count=await _count(session, RawSample.id),
        labeled_sample_count=await _count(session, LabeledSample.id),
        unlabeled_sample_count=await _unlabeled_count(session),
        split_counts=await _split_counts(session),
        last_training_job=await _last_training_job(session),
        pending_feedback_count=await _pending_feedback(session),
    )


async def _models(session: AsyncSession) -> list[ModelStatusResult]:
    """Một dòng cho MỖI stage, kể cả stage chưa có model nào.

    Bỏ hẳn stage chưa promote ra khỏi danh sách sẽ khiến màn hình quản trị im lặng về đúng
    thứ người xem cần biết: stage đó đang chạy bằng luật.
    """
    result = await session.execute(
        select(ModelVersion).where(ModelVersion.status == ModelStatus.ACTIVE.value)
    )
    active = {ModelStage(row.stage): row for row in result.scalars().all()}

    rows: list[ModelStatusResult] = []
    for stage in ModelStage:
        version = active.get(stage)
        if version is None:
            rows.append(ModelStatusResult(stage=stage.value, version=None))
            continue

        evaluation = (
            await session.execute(
                select(EvaluationRun)
                .where(EvaluationRun.model_version_id == version.id)
                .order_by(EvaluationRun.created_at.desc())
                .limit(1)
            )
        ).scalar_one_or_none()

        rows.append(
            ModelStatusResult(
                stage=stage.value,
                version=version.version,
                trained_at=version.trained_at,
                accuracy=evaluation.accuracy if evaluation else None,
                macro_f1=evaluation.macro_f1 if evaluation else None,
                evaluated_on_split=evaluation.split if evaluation else None,
            )
        )
    return rows


async def _count(session: AsyncSession, column) -> int:
    return int((await session.execute(select(func.count(column)))).scalar_one())


async def _unlabeled_count(session: AsyncSession) -> int:
    """Mẫu thô đã thu thập nhưng chưa ai gán nhãn — đây là hàng đợi việc phải làm."""
    labeled = select(LabeledSample.raw_sample_id)
    result = await session.execute(
        select(func.count(RawSample.id)).where(RawSample.id.not_in(labeled))
    )
    return int(result.scalar_one())


async def _split_counts(session: AsyncSession) -> dict[str, int]:
    result = await session.execute(
        select(SampleSplit.split, func.count(SampleSplit.id)).group_by(
            SampleSplit.split
        )
    )
    return {row[0]: int(row[1]) for row in result.all()}


async def _last_training_job(session: AsyncSession) -> TrainingJobResult | None:
    job = (
        await session.execute(
            select(TrainingJob).order_by(TrainingJob.created_at.desc()).limit(1)
        )
    ).scalar_one_or_none()
    if job is None:
        return None
    return TrainingJobResult(
        stage=job.stage,
        status=job.status,
        sample_count=job.sample_count,
        started_at=job.started_at,
        finished_at=job.finished_at,
        error_message=job.error_message,
    )


async def _pending_feedback(session: AsyncSession) -> int:
    """Phản hồi chưa chạy qua feedback_batch.py — tồn đọng lớn nghĩa là đến lúc train lại."""
    result = await session.execute(
        select(func.count(UserFeedback.id)).where(UserFeedback.processed_at.is_(None))
    )
    return int(result.scalar_one())
