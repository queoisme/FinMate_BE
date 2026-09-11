"""Truy cập ``pipeline_requests`` — nhật ký chạy thật và nguồn dò trùng."""

import uuid
from datetime import datetime, timedelta

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.enums import PipelineResult
from app.db.models.pipeline_request import PipelineRequest

# ARCHITECTURE.md §3.2 bước 4 và docx Flow 1 bước 4.3 đều chốt 5 phút.
DUPLICATE_WINDOW = timedelta(minutes=5)


async def add(session: AsyncSession, request: PipelineRequest) -> PipelineRequest:
    session.add(request)
    await session.flush()
    return request


async def get_by_backend_request_id(
    session: AsyncSession, backend_request_id: uuid.UUID
) -> PipelineRequest | None:
    result = await session.execute(
        select(PipelineRequest).where(
            PipelineRequest.backend_request_id == backend_request_id
        )
    )
    return result.scalar_one_or_none()


async def get_backend_request_id(
    session: AsyncSession, row_id: uuid.UUID
) -> uuid.UUID | None:
    result = await session.execute(
        select(PipelineRequest.backend_request_id).where(PipelineRequest.id == row_id)
    )
    return result.scalar_one_or_none()


async def find_duplicate(
    session: AsyncSession,
    user_id_hash: str,
    amount_cents: int,
    transacted_at: datetime,
    exclude_backend_request_id: uuid.UUID,
) -> PipelineRequest | None:
    """Giao dịch cùng người, cùng số tiền, lệch nhau dưới 5 phút.

    KHÔNG lọc theo ``package_name``: ca trùng kinh điển là user nhận một thông báo từ app
    ngân hàng và một từ ví liên kết cho cùng một lần quẹt thẻ (docx Flow 1 bước 4.3) —
    lọc theo package sẽ bỏ sót đúng ca cần bắt.

    Chỉ so với dòng CHƯA bị đánh dấu trùng, để ba thông báo cùng một giao dịch đều trỏ về
    một bản gốc thay vì nối thành chuỗi.
    """
    stmt = (
        select(PipelineRequest)
        .where(
            PipelineRequest.user_id_hash == user_id_hash,
            PipelineRequest.amount_cents == amount_cents,
            PipelineRequest.transacted_at.is_not(None),
            PipelineRequest.transacted_at >= transacted_at - DUPLICATE_WINDOW,
            PipelineRequest.transacted_at <= transacted_at + DUPLICATE_WINDOW,
            PipelineRequest.pipeline_result == PipelineResult.FINANCIAL.value,
            PipelineRequest.is_potential_duplicate.is_(False),
            PipelineRequest.backend_request_id != exclude_backend_request_id,
        )
        .order_by(PipelineRequest.transacted_at.desc())
        .limit(1)
    )
    result = await session.execute(stmt)
    return result.scalar_one_or_none()
