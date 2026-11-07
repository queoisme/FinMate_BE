"""API quản trị "Học AI" (nội bộ, chỉ đọc): danh sách câu chat chưa hiểu và phản hồi sửa kết quả AI của người dùng, cho trang admin.

Chỉ backend gọi, sau khi đã xác thực admin. Server cắt trang và tìm kiếm (``q``, ``limit``, ``offset``): trang admin gọi lại API chứ không lọc ở trình duyệt.
Không bao giờ trả ``user_id_hash`` hay mã giao dịch: người xem cần nội dung để duyệt, không cần biết câu của ai. Duyệt/loại câu vẫn đi qua ``scripts/nlu_candidates.py``.
"""

from typing import Annotated, Literal

from fastapi import APIRouter, Depends, Query
from sqlalchemy import func, or_, select
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.deps import get_db, verify_internal_api_key
from app.bill.rules_admin import ilike_contains
from app.db.models.nlu_candidate import NluCandidate
from app.db.models.user_feedback import UserFeedback

router = APIRouter(prefix="/admin/learning", tags=["learning-admin"], dependencies=[Depends(verify_internal_api_key)])

Session = Annotated[AsyncSession, Depends(get_db)]
Limit = Annotated[int, Query(ge=1, le=200)]
Offset = Annotated[int, Query(ge=0)]


async def _page(session: AsyncSession, query, where: list, model, limit: int, offset: int) -> tuple[list, int]:
    total = (await session.execute(select(func.count()).select_from(model).where(*where))).scalar_one()
    rows = (await session.execute(query.where(*where).limit(limit).offset(offset))).scalars().all()
    return list(rows), total


@router.get("/unclear")
async def list_unclear(
    session: Session,
    q: str | None = None,
    status: Literal["pending", "rejected"] | None = None,
    reason: Literal["low_confidence", "disagree", "not_understood"] | None = None,
    limit: Limit = 20,
    offset: Offset = 0,
) -> dict:
    """Câu chưa hiểu (mới nhất trước). ``q`` tìm trong nội dung câu; ``status`` và ``reason`` lọc theo trạng thái duyệt và lý do chưa rõ."""
    where = []
    if q and q.strip():
        where.append(NluCandidate.text.ilike(ilike_contains(q), escape="\\"))
    if status:
        where.append(NluCandidate.status == status)
    if reason:
        where.append(NluCandidate.unclear_reason == reason)
    query = select(NluCandidate).order_by(NluCandidate.created_at.desc(), NluCandidate.id)
    rows, total = await _page(session, query, where, NluCandidate, limit, offset)
    items = [
        {
            "id": str(r.id),
            "text": r.text,
            "predicted_intent": r.predicted_intent,  # đoán của máy, KHÔNG phải nhãn
            "predicted_confidence": r.predicted_confidence,
            "unclear_reason": r.unclear_reason,
            "status": r.status,
            "created_at": r.created_at.isoformat(),
        }
        for r in rows
    ]
    return {"items": items, "total": total, "limit": limit, "offset": offset}


@router.get("/feedback")
async def list_feedback(
    session: Session,
    q: str | None = None,
    feedback_type: Literal["category_correction", "amount_correction", "not_a_transaction"] | None = None,
    processed: bool | None = None,
    limit: Limit = 20,
    offset: Offset = 0,
) -> dict:
    """Phản hồi sửa kết quả AI (mới nhất trước). ``q`` tìm theo gói ứng dụng hoặc danh mục đoán/sửa; ``processed`` lọc phản hồi đã/chưa được biến thành mẫu huấn luyện."""
    where = []
    if q and q.strip():
        pattern = ilike_contains(q)
        where.append(or_(*(column.ilike(pattern, escape="\\") for column in (UserFeedback.package_name, UserFeedback.predicted_category, UserFeedback.corrected_category))))
    if feedback_type:
        where.append(UserFeedback.feedback_type == feedback_type)
    if processed is not None:
        where.append(UserFeedback.processed_at.is_not(None) if processed else UserFeedback.processed_at.is_(None))
    query = select(UserFeedback).order_by(UserFeedback.created_at.desc(), UserFeedback.id)
    rows, total = await _page(session, query, where, UserFeedback, limit, offset)
    items = [
        {
            "id": str(r.id),
            "package_name": r.package_name,
            "predicted_category": r.predicted_category,
            "corrected_category": r.corrected_category,
            "feedback_type": r.feedback_type,
            "processed": r.processed_at is not None,
            "created_at": r.created_at.isoformat(),
        }
        for r in rows
    ]
    return {"items": items, "total": total, "limit": limit, "offset": offset}
