"""AI Financial Agent phía ai-service: cổng phạm vi và bộ lập kế hoạch (Task 7); soạn câu và kiểm chứng thêm ở Task 8.

Không route nào nhận ``user_id`` (ai-service không bao giờ thấy id thật) hay ghi nội dung câu hỏi vào log: chỉ ghi mã kết quả cố định. Backend điều phối vòng agent (giữ ``userId`` từ JWT và chạy công cụ);
LLM hỏng thì route plan trả 503 ``llm_unavailable`` và backend rơi về câu mẫu từ bằng chứng."""

from __future__ import annotations

import time
from typing import Annotated

import structlog
from fastapi import APIRouter, Depends, HTTPException, Query
from sqlalchemy.ext.asyncio import AsyncSession

from app.agent import gate, memory
from app.agent import trace as agent_trace
from app.agent.trace import TraceInfo, get_trace
from app.agent.llm.provider import LlmFailed, LlmProvider, build_provider
from app.agent.planner import make_plan
from app.agent.composer import compose
from app.agent.ticket_assist import TicketAssistRequest, TicketAssistResponse, assist
from app.agent.schemas import ComposeRequest, ComposeResponse, GateRequest, GateResponse, MemoryDeleted, MemoryEntry, MemoryPutRequest, PlanRequest, PlanResponse
from app.api.deps import get_db, verify_internal_api_key
from app.core.config import settings
from app.services import secret_store
from app.services.nlu import open_nlu

logger = structlog.get_logger(__name__)
router = APIRouter(dependencies=[Depends(verify_internal_api_key)])

_provider = build_provider(settings, secret_store.getter)  # chỉ dựng đối tượng, không gọi mạng; khóa tra lúc gọi nên đổi khóa qua trang quản trị có hiệu lực ngay
_nlu = open_nlu(settings.rasa_url)


def _ms(started: float) -> int:
    return round((time.monotonic() - started) * 1000)


def get_agent_provider() -> LlmProvider:
    """Test thay bằng bản giả qua ``dependency_overrides``."""
    return _provider


def get_gate_nlu():
    """Test thay bằng bản giả qua ``dependency_overrides``."""
    return _nlu


@router.post("/agent/gate", response_model=GateResponse)
async def agent_gate(
    body: GateRequest,
    session: Annotated[AsyncSession, Depends(get_db)],
    trace: Annotated[TraceInfo | None, Depends(get_trace)],
    nlu=Depends(get_gate_nlu),
) -> GateResponse:
    started = time.monotonic()
    rules = await gate.load_rules(session)
    result = await gate.evaluate(body.text, rules, nlu)
    logger.info("agent_gate", in_scope=result.in_scope, reason=result.reason_code)  # mã cố định, không có nội dung câu hỏi
    await agent_trace.record(session, trace, "gate", _ms(started), result.reason_code)
    return GateResponse(
        in_scope=result.in_scope,
        reason_code=result.reason_code,
        refusal_text=None if result.in_scope else gate.refusal_text(rules, result.reason_code),
    )


@router.post("/agent/compose", response_model=ComposeResponse)
async def agent_compose(
    body: ComposeRequest,
    provider: Annotated[LlmProvider, Depends(get_agent_provider)],
    session: Annotated[AsyncSession, Depends(get_db)],
    trace: Annotated[TraceInfo | None, Depends(get_trace)],
) -> ComposeResponse:
    """Soạn câu từ bằng chứng. LLM hỏng KHÔNG làm lượt hỏi hỏng: trả câu mẫu dựng từ bằng chứng (``used_template``). Bộ kiểm chứng chỉ tắt được khi ``AGENT_EVAL_ENABLED``."""
    started = time.monotonic()
    verifier_on = settings.agent_verifier_enabled and not (body.skip_verifier and settings.agent_eval_enabled)
    result = await compose(provider, body.question, body.evidence, settings.agent_max_output_tokens, verifier_on)
    logger.info("agent_compose", template=result.used_template, no_data=result.no_data, claims=len(result.claims), verifier=verifier_on)
    verdict = "no_data" if result.no_data else "template" if result.used_template else "ok"
    await agent_trace.record(
        session, trace, "compose", _ms(started), verdict,
        result.usage.input_tokens if result.usage else 0, result.usage.output_tokens if result.usage else 0,
    )
    return result


HASH_QUERY = Query(pattern=r"^[0-9a-f]{64}$", description="SHA-256 hex của id người dùng")


@router.get("/agent/memory", response_model=list[MemoryEntry])
async def agent_memory_list(session: Annotated[AsyncSession, Depends(get_db)], user_id_hash: Annotated[str, HASH_QUERY]) -> list[MemoryEntry]:
    return [MemoryEntry(key=r.key, value=r.value, updated_at=r.updated_at) for r in await memory.list_memory(session, user_id_hash)]


@router.put("/agent/memory/{key}", response_model=MemoryEntry)
async def agent_memory_put(key: str, body: MemoryPutRequest, session: Annotated[AsyncSession, Depends(get_db)]) -> MemoryEntry:
    value = body.value.strip()
    if not memory.KEY_PATTERN.match(key) or not value:
        raise HTTPException(status_code=422, detail={"code": "invalid_memory", "reason": "key_or_value"})
    await memory.put_memory(session, body.user_id_hash, key, value)
    row = next(r for r in await memory.list_memory(session, body.user_id_hash) if r.key == key)
    return MemoryEntry(key=row.key, value=row.value, updated_at=row.updated_at)


@router.delete("/agent/memory/{key}", response_model=MemoryDeleted)
async def agent_memory_delete_one(key: str, session: Annotated[AsyncSession, Depends(get_db)], user_id_hash: Annotated[str, HASH_QUERY]) -> MemoryDeleted:
    return MemoryDeleted(deleted=await memory.delete_memory(session, user_id_hash, key))


@router.delete("/agent/memory", response_model=MemoryDeleted)
async def agent_memory_delete_all(session: Annotated[AsyncSession, Depends(get_db)], user_id_hash: Annotated[str, HASH_QUERY]) -> MemoryDeleted:
    return MemoryDeleted(deleted=await memory.delete_memory(session, user_id_hash))


@router.post("/agent/plan", response_model=PlanResponse)
async def agent_plan(
    body: PlanRequest,
    provider: Annotated[LlmProvider, Depends(get_agent_provider)],
    session: Annotated[AsyncSession, Depends(get_db)],
    trace: Annotated[TraceInfo | None, Depends(get_trace)],
) -> PlanResponse:
    started = time.monotonic()
    try:
        plan = await make_plan(provider, body, settings.agent_max_output_tokens)
    except LlmFailed as error:
        await agent_trace.record(session, trace, "plan", _ms(started), "llm_unavailable")
        raise HTTPException(status_code=503, detail={"code": "llm_unavailable", "reason": str(error.args[0]) if error.args else "unknown"}) from error
    logger.info("agent_plan", steps=len(plan.steps), clarify=plan.clarify is not None, input_tokens=plan.usage.input_tokens, output_tokens=plan.usage.output_tokens)
    await agent_trace.record(session, trace, "plan", _ms(started), "clarify" if plan.clarify else "ok", plan.usage.input_tokens, plan.usage.output_tokens)
    return plan


@router.post("/agent/ticket-assist", response_model=TicketAssistResponse)
async def agent_ticket_assist(body: TicketAssistRequest, provider: Annotated[LlmProvider, Depends(get_agent_provider)]) -> TicketAssistResponse:
    """Tóm tắt khiếu nại và GỢI Ý câu trả lời cho nhân sự hỗ trợ. Không gửi gì ra ngoài, không ghi nội dung vào log. LLM hỏng → 503 và khiếu nại vẫn xử lý được không cần trợ lý."""
    try:
        result = await assist(provider, body, settings.agent_max_output_tokens)
    except LlmFailed as error:
        raise HTTPException(status_code=503, detail={"code": "llm_unavailable", "reason": str(error.args[0]) if error.args else "unknown"}) from error
    logger.info("agent_ticket_assist", verified=result.verified)
    return result
