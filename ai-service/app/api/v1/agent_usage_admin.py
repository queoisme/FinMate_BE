"""API quản trị "Token & chi phí AI" (nội bộ, chỉ đọc): tổng hợp token của trợ lý tài chính từ ``agent_runs`` cho trang admin.

Chỉ backend gọi, sau khi đã xác thực admin. Bảng ``agent_runs`` chỉ có tên bước, tên công cụ, độ trễ và token (không câu hỏi, không số tiền, không mã người dùng thật), và API này
cũng không trả băm người dùng. Giá tiền KHÔNG nằm ở đây: backend lấy giá theo mô hình từ cấu hình hệ thống rồi nhân với số token. ``model`` là mô hình đang cấu hình (``agent_model``);
``agent_runs`` chưa ghi mô hình theo từng dòng, nên giá áp cho cả khoảng là giá của mô hình hiện tại.
"""

from datetime import UTC, datetime, timedelta
from typing import Annotated

from fastapi import APIRouter, Depends, Query
from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.deps import get_db, verify_internal_api_key
from app.core.config import settings
from app.db.models.agent import AgentRun

router = APIRouter(prefix="/admin/agent-usage", tags=["agent-usage-admin"], dependencies=[Depends(verify_internal_api_key)])

Session = Annotated[AsyncSession, Depends(get_db)]
Days = Annotated[int, Query(ge=1, le=366)]

_IN, _OUT = func.coalesce(func.sum(AgentRun.input_tokens), 0), func.coalesce(func.sum(AgentRun.output_tokens), 0)


@router.get("/summary")
async def summary(session: Session, days: Days = 30) -> dict:
    """Token của trợ lý trong ``days`` ngày gần nhất: tổng, theo bước (gate/plan/compose) và theo ngày (giờ UTC). ``runs`` = số lượt hỏi (``trace_id`` khác nhau)."""
    since = datetime.now(UTC) - timedelta(days=days)
    window = AgentRun.created_at >= since

    total = (await session.execute(select(func.count(func.distinct(AgentRun.trace_id)), _IN, _OUT).where(window))).one()
    by_step = (await session.execute(select(AgentRun.step, func.count(), _IN, _OUT).where(window).group_by(AgentRun.step).order_by(AgentRun.step))).all()
    day = func.date_trunc("day", AgentRun.created_at)
    by_day = (await session.execute(select(day, func.count(func.distinct(AgentRun.trace_id)), _IN, _OUT).where(window).group_by(day).order_by(day.desc()))).all()

    return {
        "days": days,
        "model": settings.agent_model,
        "provider": settings.agent_provider,
        "total": {"runs": total[0], "input_tokens": int(total[1]), "output_tokens": int(total[2])},
        "by_step": [{"step": s, "rows": n, "input_tokens": int(i), "output_tokens": int(o)} for s, n, i, o in by_step],
        "by_day": [{"day": d.date().isoformat(), "runs": n, "input_tokens": int(i), "output_tokens": int(o)} for d, n, i, o in by_day],
    }
