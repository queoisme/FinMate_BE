"""AI Financial Agent phía ai-service: cổng phạm vi và bộ lập kế hoạch (Task 7); soạn câu và kiểm chứng thêm ở Task 8.

Không route nào nhận ``user_id`` (ai-service không bao giờ thấy id thật) hay ghi nội dung câu hỏi vào log: chỉ ghi mã kết quả cố định. Backend điều phối vòng agent (giữ ``userId`` từ JWT và chạy công cụ);
LLM hỏng thì route plan trả 503 ``llm_unavailable`` và backend rơi về câu mẫu từ bằng chứng."""

from __future__ import annotations

from typing import Annotated

import structlog
from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy.ext.asyncio import AsyncSession

from app.agent import gate
from app.agent.llm.provider import LlmFailed, LlmProvider, build_provider
from app.agent.planner import make_plan
from app.agent.schemas import GateRequest, GateResponse, PlanRequest, PlanResponse
from app.api.deps import get_db, verify_internal_api_key
from app.core.config import settings
from app.services import secret_store
from app.services.nlu import open_nlu

logger = structlog.get_logger(__name__)
router = APIRouter(dependencies=[Depends(verify_internal_api_key)])

_provider = build_provider(settings, secret_store.getter)  # chỉ dựng đối tượng, không gọi mạng; khóa tra lúc gọi nên đổi khóa qua trang quản trị có hiệu lực ngay
_nlu = open_nlu(settings.rasa_url)


def get_agent_provider() -> LlmProvider:
    """Test thay bằng bản giả qua ``dependency_overrides``."""
    return _provider


def get_gate_nlu():
    """Test thay bằng bản giả qua ``dependency_overrides``."""
    return _nlu


@router.post("/agent/gate", response_model=GateResponse)
async def agent_gate(body: GateRequest, session: Annotated[AsyncSession, Depends(get_db)], nlu=Depends(get_gate_nlu)) -> GateResponse:
    rules = await gate.load_rules(session)
    result = await gate.evaluate(body.text, rules, nlu)
    logger.info("agent_gate", in_scope=result.in_scope, reason=result.reason_code)  # mã cố định, không có nội dung câu hỏi
    return GateResponse(
        in_scope=result.in_scope,
        reason_code=result.reason_code,
        refusal_text=None if result.in_scope else gate.refusal_text(rules, result.reason_code),
    )


@router.post("/agent/plan", response_model=PlanResponse)
async def agent_plan(body: PlanRequest, provider: Annotated[LlmProvider, Depends(get_agent_provider)]) -> PlanResponse:
    try:
        plan = await make_plan(provider, body, settings.agent_max_output_tokens)
    except LlmFailed as error:
        raise HTTPException(status_code=503, detail={"code": "llm_unavailable", "reason": str(error.args[0]) if error.args else "unknown"}) from error
    logger.info("agent_plan", steps=len(plan.steps), clarify=plan.clarify is not None, input_tokens=plan.usage.input_tokens, output_tokens=plan.usage.output_tokens)
    return plan
