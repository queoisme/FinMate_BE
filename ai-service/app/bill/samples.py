"""Mẫu huấn luyện của luồng hóa đơn: chữ ĐÃ lọc dữ liệu cá nhân + nhãn cuối do NGƯỜI DÙNG chọn, chỉ khi họ đồng ý.

Một lần quét (``scan_id`` = id bản ghi quét ở backend) = một hàng. Lúc quét hàng chưa có nhãn; khi người dùng lưu hoặc sửa danh mục thì gắn nhãn
cuối (nhãn sau đè nhãn trước, vẫn một hàng). Hàng chưa gắn nhãn quá hạn bị dọn (``purge_pending``). Không bao giờ ghi tên cửa hàng, chữ hóa đơn,
số tiền hay danh mục vào log; ảnh không bao giờ được lưu ở đây.
"""

from __future__ import annotations

import re
import uuid
from dataclasses import dataclass

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

from app.bill.knowledge import merchant_key
from app.db.models.bill import BILL_CHOICE_SLUGS

MAX_LINES = 200
PENDING_RETENTION_DAYS = 7  # mẫu chưa gắn nhãn (lần quét không được lưu) bị xóa sau từng ấy ngày
MAX_LINE_CHARS = 200

# Từ 9 chữ số trở lên (điện thoại, số tài khoản, mã vạch), kể cả khi xen khoảng trắng/chấm/gạch: ocr-service đã che, đây là lưới an toàn thứ hai.
# Số tiền thường dưới 9 chữ số ("1.250.000" = 7); chuỗi nhiều chữ số hơn bị từ chối chứ không cố cắt.
_LONG_DIGITS = re.compile(r"(?:\d[ .\-]?){9,}")


@dataclass(frozen=True)
class PendingSample:
    scan_id: uuid.UUID
    user_id_hash: str
    lines: list[str]
    merchant_name: str | None = None
    total_amount: int | None = None
    predicted_slug: str | None = None
    predicted_source: str | None = None


def has_long_digit_run(lines: list[str]) -> bool:
    return any(_LONG_DIGITS.search(line) for line in lines)


async def add_pending(session: AsyncSession, sample: PendingSample) -> bool:
    """Lưu mẫu chờ nhãn. False khi bị từ chối (chữ còn số dài/trống/quá lớn) hoặc ``scan_id`` đã thuộc người khác; gửi lại cùng người thì True mà không tạo hàng thứ hai."""
    lines = [line[:MAX_LINE_CHARS] for line in sample.lines]
    if not lines or len(lines) > MAX_LINES or has_long_digit_run(lines):
        return False
    key = merchant_key(sample.merchant_name) if sample.merchant_name else None
    inserted = (
        await session.execute(
            text(
                "INSERT INTO bill_training_samples (scan_id, user_id_hash, merchant_key, lines, total_amount, predicted_slug, predicted_source) "
                "VALUES (:scan_id, :user, :key, :lines, :total, :slug, :source) ON CONFLICT (scan_id) DO NOTHING RETURNING scan_id"
            ),
            {"scan_id": sample.scan_id, "user": sample.user_id_hash, "key": key or None, "lines": "\n".join(lines),
             "total": sample.total_amount, "slug": sample.predicted_slug, "source": sample.predicted_source},
        )
    ).first()
    if inserted:
        await session.commit()
        await purge_pending(session, days=PENDING_RETENTION_DAYS)  # không có scheduler: dọn mẫu chưa nhãn quá hạn mỗi khi có mẫu mới
        return True
    owner = (await session.execute(text("SELECT user_id_hash FROM bill_training_samples WHERE scan_id = :scan_id"), {"scan_id": sample.scan_id})).scalar_one_or_none()
    return owner == sample.user_id_hash


async def label(session: AsyncSession, scan_id: uuid.UUID, user_id_hash: str, final_slug: str) -> bool:
    """Gắn nhãn cuối do người dùng chọn (``confirmed`` nếu trùng dự đoán, ngược lại ``corrected``). False khi không có mẫu của người đó hoặc slug không hợp lệ."""
    if final_slug not in BILL_CHOICE_SLUGS:
        return False
    updated = (
        await session.execute(
            text(
                "UPDATE bill_training_samples SET final_slug = CAST(:slug AS varchar), labeled_at = now(), "
                "label_kind = CASE WHEN predicted_slug = CAST(:slug AS varchar) THEN 'confirmed' ELSE 'corrected' END "
                "WHERE scan_id = :scan_id AND user_id_hash = :user RETURNING scan_id"
            ),
            {"slug": final_slug, "scan_id": scan_id, "user": user_id_hash},
        )
    ).first()
    await session.commit()
    return updated is not None


async def delete_by_user_hash(session: AsyncSession, user_id_hash: str) -> int:
    """Xóa mọi mẫu của một người dùng (luồng xóa dữ liệu). Trả số hàng đã xóa."""
    result = await session.execute(text("DELETE FROM bill_training_samples WHERE user_id_hash = :user"), {"user": user_id_hash})
    await session.commit()
    return result.rowcount or 0


async def purge_pending(session: AsyncSession, days: int = 7) -> int:
    """Xóa mẫu CHƯA gắn nhãn cũ hơn ``days`` ngày (lần quét không bao giờ được lưu). Mẫu đã có nhãn được giữ."""
    result = await session.execute(
        text("DELETE FROM bill_training_samples WHERE final_slug IS NULL AND created_at < now() - make_interval(days => :days)"), {"days": days}
    )
    await session.commit()
    return result.rowcount or 0
