"""``POST /api/v1/feedback`` — người dùng sửa lại kết quả AI.

Best-effort theo đúng cách backend gọi: ``AIServiceClient.SendFeedbackAsync`` nuốt mọi lỗi
để không chặn luồng sửa giao dịch. Vì vậy ở đây ưu tiên ghi nhận được nhiều nhất có thể
thay vì từ chối request chỉ vì một trường không chuẩn.
"""

from __future__ import annotations

from datetime import timedelta
from typing import Annotated

import structlog
from fastapi import APIRouter, Depends
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.deps import get_db, verify_internal_api_key
from app.core.enums import CATEGORY_SLUGS, FeedbackType, PipelineResult
from app.db.models.pipeline_request import PipelineRequest
from app.db.models.user_feedback import UserFeedback
from app.db.repositories import feedback_repo, pipeline_repo
from app.schemas.request import FeedbackRequest
from app.schemas.response import FeedbackResponse

logger = structlog.get_logger(__name__)
router = APIRouter(dependencies=[Depends(verify_internal_api_key)])

# Cửa sổ dò ngược khi backend không gửi kèm id — xem _resolve_pipeline_request.
FALLBACK_LOOKBACK = timedelta(days=30)


@router.post("/feedback", response_model=FeedbackResponse)
async def submit_feedback(
    request: FeedbackRequest, session: Annotated[AsyncSession, Depends(get_db)]
) -> FeedbackResponse:
    pipeline_request_id = await _resolve_pipeline_request(session, request)

    row = UserFeedback(
        backend_transaction_id_hash=request.backend_transaction_id_hash,
        user_id_hash=request.user_id_hash,
        pipeline_request_id=pipeline_request_id,
        package_name=request.package_name,
        predicted_category=_known_slug(request.predicted_category),
        corrected_category=_known_slug(request.corrected_category),
        feedback_type=_known_feedback_type(request.feedback_type),
    )
    await feedback_repo.add(session, row)
    await session.commit()

    logger.info(
        "feedback_received",
        feedback_type=row.feedback_type,
        package_name=row.package_name,
        linked=pipeline_request_id is not None,
    )
    return FeedbackResponse(feedback_id=row.id)


async def _resolve_pipeline_request(session: AsyncSession, request: FeedbackRequest):
    """Tìm dòng ``pipeline_requests`` mà phản hồi này nói về.

    Backend gửi ``pipeline_request_id`` = id ``notification_log`` của nó, khớp với
    ``pipeline_requests.backend_request_id``.

    **Hiện tại backend luôn gửi null** (``UpdateTransactionCommandHandler`` truyền thẳng
    ``null``) dù contract §3.3 đã có sẵn trường này. Không có id thì phải dò ngược bằng
    ``(user_id_hash, package_name, predicted_category)`` và lấy dòng gần nhất — đúng trong
    phần lớn trường hợp, nhưng sẽ gán nhầm nếu cùng một người có hai giao dịch cùng danh
    mục ở cùng một ví chưa được sửa. Cách khắc phục sạch nằm ở phía backend: truyền
    ``transaction.NotificationLogId`` vào đúng trường đã có sẵn trong contract.
    """
    if request.pipeline_request_id is not None:
        row = await pipeline_repo.get_by_backend_request_id(
            session, request.pipeline_request_id
        )
        if row is not None:
            return row.id

    if request.predicted_category is None:
        return None

    stmt = (
        select(PipelineRequest.id)
        .where(
            PipelineRequest.user_id_hash == request.user_id_hash,
            PipelineRequest.package_name == request.package_name,
            PipelineRequest.category_slug == request.predicted_category,
            PipelineRequest.pipeline_result == PipelineResult.FINANCIAL.value,
        )
        .order_by(PipelineRequest.created_at.desc())
        .limit(1)
    )
    result = await session.execute(stmt)
    return result.scalar_one_or_none()


def _known_slug(slug: str | None) -> str | None:
    """Danh mục TỰ TẠO của người dùng có slug tuỳ ý, không nằm trong 11 slug hệ thống.

    Taxonomy của AI chỉ có 11 slug đó nên không học được danh mục riêng; ghi thẳng vào DB
    lại vi phạm CHECK constraint và làm request 500. Bỏ về NULL: tín hiệu "AI đoán food
    nhưng người dùng chuyển đi chỗ khác" vẫn giữ được ở vế predicted.
    """
    if slug is None:
        return None
    if slug in CATEGORY_SLUGS:
        return slug
    logger.info("feedback_custom_category_dropped")
    return None


def _known_feedback_type(value: str) -> str:
    try:
        return FeedbackType(value).value
    except ValueError:
        logger.warning("feedback_type_unknown", feedback_type=value)
        return FeedbackType.CATEGORY_CORRECTION.value
