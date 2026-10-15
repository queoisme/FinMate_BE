"""Bộ đếm mức dùng của ``/bill/classify-items``: CHỈ số đếm theo ngày (món do bộ nhớ, luật, Gemini... trả lời), không tên món, cửa hàng hay người dùng.

Ghi theo kiểu cộng dồn trong Postgres (``bill_classify_stats``). Việc ghi là phần phụ: lỗi hay chậm thì bỏ qua, không bao giờ làm hỏng việc phân loại.
"""

from __future__ import annotations

import asyncio
from collections.abc import Sequence

import structlog
from sqlalchemy import text

from app.db.session import AsyncSessionLocal
from app.schemas.bill import ClassifyItemOut

logger = structlog.get_logger(__name__)

RECORD_TIMEOUT_SECONDS = 1.0

_UPSERT = text(
    "INSERT INTO bill_classify_stats (day, metric, value) VALUES (current_date, :metric, :value) "
    "ON CONFLICT (day, metric) DO UPDATE SET value = bill_classify_stats.value + EXCLUDED.value"
)


def build_counts(items: Sequence[ClassifyItemOut], allow_ai: bool, ai_used: bool, extra: dict[str, int]) -> dict[str, int]:
    """Số đếm của MỘT yêu cầu; chỉ giữ giá trị khác 0 để bảng không đầy dòng toàn số 0."""
    counts: dict[str, int] = {"requests": 1, "items": len(items)}
    for item in items:
        key = f"src_{item.source}"
        counts[key] = counts.get(key, 0) + 1
    if ai_used:
        counts["ai_requests"] = 1
    if not allow_ai:
        counts["noconsent_requests"] = 1
    counts.update(extra)
    return {k: v for k, v in counts.items() if v}


class ClassifyStatsRecorder:
    def __init__(self, session_factory=AsyncSessionLocal) -> None:
        self._session_factory = session_factory

    async def record(self, counts: dict[str, int]) -> None:
        async def write() -> None:
            async with self._session_factory() as session:
                for metric, value in counts.items():
                    await session.execute(_UPSERT, {"metric": metric, "value": value})
                await session.commit()

        try:
            await asyncio.wait_for(write(), RECORD_TIMEOUT_SECONDS)
        except Exception as error:  # noqa: BLE001 - số đo là phần phụ
            logger.warning("classify_stats_failed", error_type=type(error).__name__)
