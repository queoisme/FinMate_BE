"""API quản trị "Học AI" (nội bộ, chỉ đọc): danh sách câu chat chưa hiểu và phản hồi sửa kết quả AI của người dùng, cho trang admin.

Chỉ backend gọi, sau khi đã xác thực admin. Server cắt trang và tìm kiếm (``q``, ``limit``, ``offset``): trang admin gọi lại API chứ không lọc ở trình duyệt.
Không bao giờ trả ``user_id_hash`` hay mã giao dịch: người xem cần nội dung để duyệt, không cần biết câu của ai. Duyệt/loại câu vẫn đi qua ``scripts/nlu_candidates.py``.
"""

from typing import Annotated, Literal

from fastapi import APIRouter, Depends, Query
from sqlalchemy import func, or_, select, text
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.deps import get_db, verify_internal_api_key
from app.bill.rules_admin import ilike_contains
from app.db.models.user_feedback import UserFeedback
from app.db.models.pipeline_request import PipelineRequest
from app.services import learning_suggestions

router = APIRouter(prefix="/admin/learning", tags=["learning-admin"], dependencies=[Depends(verify_internal_api_key)])

Session = Annotated[AsyncSession, Depends(get_db)]
Limit = Annotated[int, Query(ge=1, le=200)]
Offset = Annotated[int, Query(ge=0)]


async def _page(session: AsyncSession, query, where: list, model, limit: int, offset: int) -> tuple[list, int]:
    total = (await session.execute(select(func.count()).select_from(model).where(*where))).scalar_one()
    rows = (await session.execute(query.where(*where).limit(limit).offset(offset))).scalars().all()
    return list(rows), total


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


@router.get("/summary")
async def summary(session: Session) -> dict:
    """Tổng quan phản hồi: số phản hồi theo loại, số chưa dùng để học, và các cặp (AI đoán → người dùng sửa) gặp nhiều nhất, để biết nên sửa luật nào trước."""
    by_type = dict((await session.execute(select(UserFeedback.feedback_type, func.count()).group_by(UserFeedback.feedback_type))).all())
    unprocessed = (await session.execute(select(func.count()).select_from(UserFeedback).where(UserFeedback.processed_at.is_(None)))).scalar_one()
    pairs = (await session.execute(
        select(UserFeedback.predicted_category, UserFeedback.corrected_category, func.count().label("times"))
        .where(UserFeedback.feedback_type == "category_correction", UserFeedback.corrected_category.is_not(None))
        .group_by(UserFeedback.predicted_category, UserFeedback.corrected_category)
        .order_by(text("times DESC")).limit(8)
    )).all()
    return {
        "total": sum(by_type.values()),
        "by_type": by_type,
        "unprocessed": unprocessed,
        "top_corrections": [{"predicted_category": p, "corrected_category": c, "times": t} for p, c, t in pairs],
    }


@router.get("/suggestions")
async def suggestions(
    session: Session,
    min_users: Annotated[int, Query(ge=learning_suggestions.MIN_USERS, le=50)] = learning_suggestions.MIN_USERS,
    include_covered: bool = False,
    limit: Limit = 20,
    offset: Offset = 0,
) -> dict:
    """Tên cửa hàng mà từ ``min_users`` người khác nhau trở lên cùng sửa sang một danh mục: ứng viên để thêm vào từ điển tên cửa hàng.

    Mặc định ẩn gợi ý đã có đúng trong từ điển (``include_covered=false``). Không trả người dùng nào, chỉ số người và số lần.
    """
    merchant = func.regexp_replace(func.lower(func.trim(PipelineRequest.merchant_name)), r"\s+", " ", "g")  # gom cả tên viết thừa khoảng trắng
    users = func.count(func.distinct(UserFeedback.user_id_hash))
    grouped = (
        select(merchant.label("merchant"), UserFeedback.corrected_category, users.label("users"), func.count().label("times"),
               func.array_agg(func.distinct(UserFeedback.predicted_category)).label("predicted"), func.max(UserFeedback.created_at).label("last_at"))
        .join(PipelineRequest, PipelineRequest.id == UserFeedback.pipeline_request_id)
        .where(UserFeedback.feedback_type == "category_correction", UserFeedback.corrected_category.is_not(None),
               PipelineRequest.merchant_name.is_not(None), func.length(func.trim(PipelineRequest.merchant_name)) > 0)
        .group_by(merchant, UserFeedback.corrected_category)
        .having(users >= min_users)
        .order_by(users.desc(), func.count().desc(), merchant)
    )
    built = [
        (learning_suggestions.build_suggestion(row.merchant, row.corrected_category, row.users, row.times, list(row.predicted or [])), row.last_at)
        for row in (await session.execute(grouped)).all()
    ]
    if not include_covered:
        built = [(s, at) for s, at in built if not s.covered]
    page = built[offset:offset + limit]  # ponytail: lọc "đã có trong từ điển" cần từ điển đang chạy (bộ nhớ tiến trình) nên cắt trang sau khi gom; số nhóm >= 2 người vốn nhỏ
    return {"items": [s.as_dict(at.isoformat() if at else None) for s, at in page], "total": len(built), "limit": limit, "offset": offset}

