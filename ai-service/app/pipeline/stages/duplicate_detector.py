"""Stage 4 — thông báo này có phải bản trùng của một giao dịch vừa ghi không.

Ca kinh điển: một lần quẹt thẻ sinh ra hai thông báo, một từ app ngân hàng và một từ ví
liên kết. Hai nội dung khác hẳn nhau nên hash nội dung phía backend không bắt được; thứ
trùng nhau là SỐ TIỀN và THỜI ĐIỂM (docx Flow 1 bước 4.3).
"""

from __future__ import annotations

import uuid
from dataclasses import dataclass
from datetime import datetime

from sqlalchemy.ext.asyncio import AsyncSession

from app.db.repositories import pipeline_repo


@dataclass(frozen=True)
class DuplicateOutcome:
    is_potential_duplicate: bool
    duplicate_request_id: uuid.UUID | None = None
    duplicate_of_row_id: uuid.UUID | None = None


async def detect(
    session: AsyncSession,
    user_id_hash: str,
    amount_cents: int | None,
    transacted_at: datetime | None,
    backend_request_id: uuid.UUID,
) -> DuplicateOutcome:
    if amount_cents is None or transacted_at is None:
        return DuplicateOutcome(False)

    existing = await pipeline_repo.find_duplicate(
        session,
        user_id_hash=user_id_hash,
        amount_cents=amount_cents,
        transacted_at=transacted_at,
        exclude_backend_request_id=backend_request_id,
    )
    if existing is None:
        return DuplicateOutcome(False)

    # Trả backend_request_id chứ không phải id nội bộ: backend chỉ tra ngược được theo id
    # notification_log của chính nó.
    return DuplicateOutcome(True, existing.backend_request_id, existing.id)
