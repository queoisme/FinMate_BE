"""``POST /api/v1/analyze`` — điểm vào duy nhất của pipeline."""

from typing import Annotated

from fastapi import APIRouter, Depends
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.deps import get_db, verify_internal_api_key
from app.pipeline.orchestrator import pipeline
from app.schemas.request import AnalyzeRequest
from app.schemas.response import AnalyzeResponse

router = APIRouter(dependencies=[Depends(verify_internal_api_key)])


@router.post("/analyze", response_model=AnalyzeResponse)
async def analyze(
    request: AnalyzeRequest, session: Annotated[AsyncSession, Depends(get_db)]
) -> AnalyzeResponse:
    """Lỗi không lường trước được CỐ Ý để thoát ra thành HTTP 500.

    Backend bắt 500 thành ``AIServiceUnavailableException`` → ``notification_logs`` sang
    ``failed`` → ``RetryFailedNotificationJob`` thử lại. Nếu ở đây nuốt lỗi rồi trả 200 kèm
    ``pipeline_result="error"``, backend sẽ đánh dấu đã xử lý và thông báo đó mất vĩnh viễn
    dù lỗi chỉ là mất kết nối DB trong vài giây.
    """
    response = await pipeline.analyze(session, request)
    await session.commit()
    return response
