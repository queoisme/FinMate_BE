"""Vết lập luận của AI Financial Agent: mỗi bước của một lượt hỏi ghi MỘT dòng ``agent_runs`` khi backend gửi ``X-Trace-Id`` (uuid) và ``X-User-Hash`` (SHA-256 hex của id người dùng).

Chỉ ghi tên bước, độ trễ, số token và một kết luận cố định (ví dụ ``finance_cue``, ``clarify``, ``template``) — KHÔNG bao giờ nội dung câu hỏi, bằng chứng hay câu trả lời, và không có id thật.
Header thiếu hoặc sai dạng thì không ghi gì; ghi vết lỗi cũng không bao giờ làm hỏng câu trả lời (vết chỉ để xem lại, không phải một phần của lượt hỏi)."""

from __future__ import annotations

import re
import uuid
from dataclasses import dataclass
from typing import Annotated

import structlog
from fastapi import Header
from sqlalchemy.ext.asyncio import AsyncSession

from app.db.models.agent import AgentRun

logger = structlog.get_logger(__name__)
_HASH = re.compile(r"^[0-9a-f]{64}$")


@dataclass(frozen=True)
class TraceInfo:
    trace_id: uuid.UUID
    user_hash: str


def get_trace(
    x_trace_id: Annotated[str | None, Header()] = None,
    x_user_hash: Annotated[str | None, Header()] = None,
) -> TraceInfo | None:
    """Dependency: ``TraceInfo`` nếu CẢ HAI header hợp lệ, ngược lại None (không ghi vết, không lỗi)."""
    if not x_trace_id or not x_user_hash or not _HASH.match(x_user_hash):
        return None
    try:
        return TraceInfo(uuid.UUID(x_trace_id), x_user_hash)
    except ValueError:
        return None


async def record(
    session: AsyncSession,
    trace: TraceInfo | None,
    step: str,
    latency_ms: int,
    verdict: str | None = None,
    input_tokens: int = 0,
    output_tokens: int = 0,
    tool: str | None = None,
) -> None:
    if trace is None:
        return
    try:
        session.add(
            AgentRun(
                trace_id=trace.trace_id,
                user_id_hash=trace.user_hash,
                step=step,
                tool=tool,
                latency_ms=max(0, latency_ms),
                input_tokens=input_tokens,
                output_tokens=output_tokens,
                verdict=verdict[:24] if verdict else None,
            )
        )
        await session.commit()
    except Exception as error:  # noqa: BLE001 - vết hỏng không được làm hỏng lượt hỏi
        await session.rollback()
        logger.warning("agent_trace_failed", error_type=type(error).__name__)
