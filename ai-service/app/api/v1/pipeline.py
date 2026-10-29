"""``POST /api/v1/analyze`` — điểm vào duy nhất của pipeline."""

from typing import Annotated

from fastapi import APIRouter, Depends
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.deps import get_db, verify_internal_api_key
from app.api.v1.bill import get_ai_instruction, get_bill_knowledge, get_chat_gemini_client
from app.bill.gemini import GeminiClient
from app.bill.knowledge import BillKnowledge
from app.pipeline.orchestrator import pipeline
from app.pipeline.stages.ai_categorizer import AiDeps
from app.schemas.request import AnalyzeRequest
from app.schemas.response import AnalyzeResponse

router = APIRouter(dependencies=[Depends(verify_internal_api_key)])


@router.post("/analyze", response_model=AnalyzeResponse)
async def analyze(
    request: AnalyzeRequest,
    session: Annotated[AsyncSession, Depends(get_db)],
    gemini: Annotated[GeminiClient, Depends(get_chat_gemini_client)],
    knowledge: Annotated[BillKnowledge, Depends(get_bill_knowledge)],
    instruction: Annotated[str, Depends(get_ai_instruction)],
) -> AnalyzeResponse:
    """Lỗi không lường trước được CỐ Ý để thoát ra thành HTTP 500.

    Backend bắt 500 thành ``AIServiceUnavailableException`` → ``notification_logs`` sang
    ``failed`` → ``RetryFailedNotificationJob`` thử lại. Nếu ở đây nuốt lỗi rồi trả 200 kèm
    ``pipeline_result="error"``, backend sẽ đánh dấu đã xử lý và thông báo đó mất vĩnh viễn
    dù lỗi chỉ là mất kết nối DB trong vài giây.
    """
    response = await pipeline.analyze(session, request, AiDeps(gemini, knowledge, instruction))
    await session.commit()
    return response
