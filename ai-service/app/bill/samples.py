"""Mẫu huấn luyện của luồng hóa đơn: chữ ĐÃ lọc dữ liệu cá nhân + nhãn cuối do NGƯỜI DÙNG chọn, chỉ khi họ đồng ý.

Một lần quét (``scan_id`` = id bản ghi quét ở backend) = một hàng. Lúc quét hàng chưa có nhãn; khi người dùng lưu hoặc sửa danh mục thì gắn nhãn
cuối (nhãn sau đè nhãn trước, vẫn một hàng). Hàng chưa gắn nhãn quá hạn bị dọn (``purge_pending``). Không bao giờ ghi tên cửa hàng, chữ hóa đơn,
số tiền hay danh mục vào log; ảnh không bao giờ được lưu ở đây.
"""

from __future__ import annotations

import asyncio
import re
import uuid
from dataclasses import dataclass

import structlog
from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

from app.bill.knowledge import merchant_key
from app.services import category_registry

logger = structlog.get_logger(__name__)

MAX_LINES = 200
PENDING_RETENTION_DAYS = 7  # mẫu chưa gắn nhãn (lần quét không được lưu) bị xóa sau từng ấy ngày
MAX_LINE_CHARS = 200

# Lưới an toàn thứ hai sau ocr-service/privacy.py (dùng cùng định nghĩa): số điện thoại, hoặc một chuỗi liền (chỉ xen chấm/gạch, KHÔNG xen khoảng trắng) có từ
# 9 chữ số trở lên (số tài khoản, mã vạch). Không cộng chữ số qua khoảng trắng: dòng món "2 45.000 90.000" (số lượng, đơn giá, thành tiền) sẽ bị loại oan.
_PHONE = re.compile(r"(?<!\d)(?:\+?84|0)[\s.\-]?\d{2,3}[\s.\-]?\d{3}[\s.\-]?\d{3,4}(?!\d)")
_RUN = re.compile(r"(?<!\d)\d[\d.\-]{7,}\d(?!\d)")
_MIN_DIGITS = 9


@dataclass(frozen=True)
class PendingSample:
    scan_id: uuid.UUID
    user_id_hash: str
    lines: list[str]
    merchant_name: str | None = None
    predicted_slug: str | None = None
    predicted_source: str | None = None


def has_long_digit_run(lines: list[str]) -> bool:
    return any(_PHONE.search(line) or any(sum(c.isdigit() for c in m.group()) >= _MIN_DIGITS for m in _RUN.finditer(line)) for line in lines)


async def add_pending(session: AsyncSession, sample: PendingSample) -> bool:
    """Lưu mẫu chờ nhãn. False khi bị từ chối (chữ còn số dài/trống/quá lớn) hoặc ``scan_id`` đã thuộc người khác; gửi lại cùng người thì True mà không tạo hàng thứ hai."""
    lines = [line[:MAX_LINE_CHARS] for line in sample.lines]
    if not lines or len(lines) > MAX_LINES or has_long_digit_run(lines):
        return False
    key = merchant_key(sample.merchant_name) if sample.merchant_name else None
    inserted = (
        await session.execute(
            text(
                "INSERT INTO ml_training_samples (scan_id, user_id_hash, merchant_key, lines, predicted_slug, predicted_source) "
                "VALUES (:scan_id, :user, :key, :lines, :slug, :source) ON CONFLICT (scan_id) DO NOTHING RETURNING scan_id"
            ),
            {"scan_id": sample.scan_id, "user": sample.user_id_hash, "key": key or None, "lines": "\n".join(lines),
             "slug": sample.predicted_slug, "source": sample.predicted_source},
        )
    ).first()
    if inserted:
        await session.commit()
        await purge_pending(session, days=PENDING_RETENTION_DAYS)  # không có scheduler: dọn mẫu chưa nhãn quá hạn mỗi khi có mẫu mới
        return True
    owner = (await session.execute(text("SELECT user_id_hash FROM ml_training_samples WHERE scan_id = :scan_id"), {"scan_id": sample.scan_id})).scalar_one_or_none()
    return owner == sample.user_id_hash


async def label(session: AsyncSession, scan_id: uuid.UUID, user_id_hash: str, final_slug: str) -> bool:
    """Gắn nhãn cuối do người dùng chọn (trùng dự đoán thì là xác nhận, khác thì là sửa: suy từ ``predicted_slug`` và ``final_slug``). False khi không có mẫu của người đó hoặc slug không hợp lệ."""
    if not category_registry.is_spend(final_slug):
        return False
    updated = (
        await session.execute(
            text(
                "UPDATE ml_training_samples SET final_slug = CAST(:slug AS varchar), labeled_at = now() "
                "WHERE scan_id = :scan_id AND user_id_hash = :user RETURNING scan_id"
            ),
            {"slug": final_slug, "scan_id": scan_id, "user": user_id_hash},
        )
    ).first()
    await session.commit()
    return updated is not None


async def discard(session: AsyncSession, scan_id: uuid.UUID, user_id_hash: str) -> bool:
    """Xóa mẫu của một lần quét (đã gắn nhãn hay chưa) khi người dùng chọn danh mục không phải hệ thống: không còn nhãn nào đáng học. False khi không có mẫu CỦA người đó."""
    result = await session.execute(
        text("DELETE FROM ml_training_samples WHERE scan_id = :scan AND user_id_hash = :user"), {"scan": scan_id, "user": user_id_hash}
    )
    await session.commit()
    return bool(result.rowcount)


async def delete_by_user_hash(session: AsyncSession, user_id_hash: str) -> int:
    """Xóa mọi mẫu của một người dùng (luồng xóa dữ liệu). Trả số hàng đã xóa."""
    result = await session.execute(text("DELETE FROM ml_training_samples WHERE user_id_hash = :user"), {"user": user_id_hash})
    await session.commit()
    return result.rowcount or 0


async def purge_pending(session: AsyncSession, days: int = 7) -> int:
    """Xóa mẫu CHƯA gắn nhãn cũ hơn ``days`` ngày (lần quét không bao giờ được lưu). Mẫu đã có nhãn được giữ."""
    result = await session.execute(
        text("DELETE FROM ml_training_samples WHERE final_slug IS NULL AND created_at < now() - make_interval(days => :days)"), {"days": days}
    )
    await session.commit()
    return result.rowcount or 0


async def purge_loop(factory, interval_seconds: float = 3600) -> None:
    """Dọn mẫu chưa nhãn quá hạn định kỳ (ai-service không có scheduler ngoài). Lỗi một lượt chỉ ghi log rồi thử lại lượt sau; hủy task để dừng."""
    while True:
        try:
            async with factory() as session:
                purged = await purge_pending(session, days=PENDING_RETENTION_DAYS)
            if purged:
                logger.info("training_samples_purged", count=purged)
        except Exception as error:  # noqa: BLE001 - một lượt dọn lỗi không được giết vòng lặp
            logger.warning("training_samples_purge_failed", error_type=type(error).__name__)
        await asyncio.sleep(interval_seconds)
