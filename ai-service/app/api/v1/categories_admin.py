"""Đồng bộ danh mục từ backend (nội bộ): chỉ backend gọi, sau khi admin tạo / sửa / tắt một danh mục hệ thống, và khi khởi động để kéo AI Service về cùng trạng thái.
Xác thực bằng khóa nội bộ; người thực hiện đi kèm ở ``X-Actor-Hash`` (SHA-256 id admin, hoặc mã băm cố định của tiến trình hệ thống) và được ghi vào nhật ký.

Hợp đồng: ``PUT /api/v1/admin/categories/sync`` với ``{"categories": [{"slug", "label", "kind": "expense" | "income", "is_active"}]}``. Danh sách là cộng thêm, không xóa."""

from typing import Annotated

from fastapi import APIRouter, Depends, Header, HTTPException, status
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.deps import get_db, verify_internal_api_key
from app.db.models.category import KbCategory
from app.schemas.categories import CategorySyncRequest, CategorySyncResponse
from app.services import category_sync
from app.services.category_sync import CategorySyncError

router = APIRouter(prefix="/admin/categories", tags=["categories-admin"], dependencies=[Depends(verify_internal_api_key)])

Session = Annotated[AsyncSession, Depends(get_db)]
Actor = Annotated[str, Header(alias="X-Actor-Hash", min_length=64, max_length=64, pattern=r"^[0-9a-f]{64}$")]


@router.get("")
async def list_categories(session: Session) -> dict:
    """Danh mục AI Service đang biết, để backend đối chiếu sau khi đồng bộ. Gồm cả dòng bí danh ``legacy`` (không đồng bộ được)."""
    rows = (await session.execute(select(KbCategory).order_by(KbCategory.kind, KbCategory.slug))).scalars().all()
    return {
        "items": [
            {"slug": r.slug, "label": r.label, "kind": r.kind, "is_active": r.is_active, "replaced_by": r.replaced_by, "taxonomy_version": r.taxonomy_version, "synced_at": r.synced_at.isoformat()}
            for r in rows
        ]
    }


@router.put("/sync", response_model=CategorySyncResponse)
async def sync_categories(body: CategorySyncRequest, actor: Actor, session: Session) -> CategorySyncResponse:
    try:
        result = await category_sync.sync(session, body.categories, taxonomy_version=body.taxonomy_version, actor=actor, reason=body.reason)
    except CategorySyncError as error:
        await session.rollback()
        raise HTTPException(status.HTTP_409_CONFLICT, detail={"code": "category_conflict", "message": str(error)}) from error
    return CategorySyncResponse(created=result.created, updated=result.updated, unchanged=result.unchanged)
