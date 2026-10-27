"""Dọn dữ liệu quá hạn ngay trong tiến trình AI Service (không cần cron ngoài).

Ba nơi giữ dữ liệu của người dùng quá hạn mà trước đây chỉ có script chạy tay:
  - ``ops_analyze_log``: số tiền, mô tả và tên cửa hàng của từng giao dịch đã phân tích;
  - ``ml_raw_samples`` CHƯA gắn nhãn (mẫu đã có nhãn là dữ liệu huấn luyện nên được giữ; mẫu ``seed`` cũng giữ);
  - ``bill_unknown_scans``: chữ hóa đơn của hàng đợi duyệt cửa hàng lạ (tính theo lần quét cuối).
Mẫu huấn luyện chưa nhãn (7 ngày) và ứng viên NLU đã có vòng dọn riêng (``app.bill.samples``, ``app.services.nlu_candidates``).

Xóa theo lô để không giữ khóa lâu trên bảng đang được ghi. Nhiều bản AI Service chạy cùng lúc thì mỗi bản dọn một lượt: xóa trùng là vô hại. Các script
``scripts/purge_*.py`` gọi lại đúng các hàm này (cho người vận hành muốn chạy tay, ``--dry-run`` hay đổi số ngày).
"""

from __future__ import annotations

import asyncio
from collections.abc import Callable
from datetime import UTC, datetime, timedelta

import structlog
from sqlalchemy import delete, func, select
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

from app.core.enums import SampleSource
from app.db.models.bill import BillUnknownScan
from app.db.models.labeled_sample import LabeledSample
from app.db.models.pipeline_request import PipelineRequest
from app.db.models.raw_sample import RawSample

logger = structlog.get_logger(__name__)

BATCH_SIZE = 5000


async def _purge(
    factory: async_sessionmaker[AsyncSession], model, id_column, conditions, dry_run: bool, batch_size: int
) -> int:
    async with factory() as session:
        if dry_run:
            return (await session.execute(select(func.count()).select_from(model).where(*conditions))).scalar_one()
        total = 0
        while True:
            batch = select(id_column).where(*conditions).limit(batch_size)
            result = await session.execute(delete(model).where(id_column.in_(batch)))
            await session.commit()
            if result.rowcount == 0:
                return total
            total += result.rowcount


async def purge_analyze_log(factory: async_sessionmaker[AsyncSession], days: int = 90, dry_run: bool = False, batch_size: int = BATCH_SIZE) -> int:
    cutoff = datetime.now(UTC) - timedelta(days=days)
    return await _purge(factory, PipelineRequest, PipelineRequest.id, (PipelineRequest.created_at < cutoff,), dry_run, batch_size)


async def purge_raw_samples(factory: async_sessionmaker[AsyncSession], days: int = 90, dry_run: bool = False, batch_size: int = BATCH_SIZE) -> int:
    cutoff = datetime.now(UTC) - timedelta(days=days)
    conditions = (
        RawSample.created_at < cutoff,
        RawSample.source != SampleSource.SEED.value,
        RawSample.id.not_in(select(LabeledSample.raw_sample_id)),
    )
    return await _purge(factory, RawSample, RawSample.id, conditions, dry_run, batch_size)


async def purge_unknown_scans(factory: async_sessionmaker[AsyncSession], days: int = 90, dry_run: bool = False, batch_size: int = BATCH_SIZE) -> int:
    cutoff = datetime.now(UTC) - timedelta(days=days)
    return await _purge(factory, BillUnknownScan, BillUnknownScan.id, (BillUnknownScan.last_seen_at < cutoff,), dry_run, batch_size)


# tên -> hàm: để log và test biết đúng ba việc nào được chạy
JOBS: dict[str, Callable] = {
    "ops_analyze_log": purge_analyze_log,
    "ml_raw_samples": purge_raw_samples,
    "bill_unknown_scans": purge_unknown_scans,
}


async def run_once(factory: async_sessionmaker[AsyncSession], days: int) -> dict[str, int]:
    """Một lượt dọn cả ba nơi. Lỗi ở một nơi được ghi log và không chặn hai nơi còn lại; trả số dòng đã xóa của từng nơi (nơi lỗi không có mặt)."""
    removed: dict[str, int] = {}
    for name, job in JOBS.items():
        try:
            removed[name] = await job(factory, days)
        except Exception as error:  # noqa: BLE001 - một nơi lỗi không được chặn các nơi khác hay giết vòng lặp
            logger.warning("retention_purge_failed", table=name, error_type=type(error).__name__)
    return removed


async def retention_loop(factory: async_sessionmaker[AsyncSession], days: int, interval_seconds: float, initial_delay_seconds: float = 300) -> None:
    """Dọn định kỳ (AI Service không có scheduler ngoài). Chờ ``initial_delay_seconds`` trước lượt đầu để không tranh việc với lúc khởi động; hủy task để dừng."""
    await asyncio.sleep(initial_delay_seconds)
    while True:
        removed = await run_once(factory, days)
        if any(removed.values()):
            logger.info("retention_purged", **removed)
        await asyncio.sleep(interval_seconds)
