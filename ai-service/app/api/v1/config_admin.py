"""API quản trị tài liệu cấu hình có phiên bản (nội bộ): chỉ backend gọi, sau khi đã xác thực admin. Xác thực ở đây là khóa nội bộ; người thực hiện đi kèm ở tiêu đề
``X-Actor-Hash`` (SHA-256 id admin).

Luồng: ``GET /documents/{name}`` (tải bản đang chạy) -> sửa -> ``POST .../drafts`` (bản nháp, được kiểm tra) -> ``POST .../preview`` -> ``POST .../activate``.
Hoàn tác là kích hoạt lại một phiên bản cũ. Lỗi là JSON ``{"error": {"code", "message"}}`` cùng mã HTTP (404/409/422), giống ``/admin/rules``."""

from typing import Annotated

from fastapi import APIRouter, Depends, Header, Query, status
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.deps import get_db, verify_internal_api_key
from app.schemas.config_admin import ActivateBody, DraftCreate
from app.services import config_documents as docs

router = APIRouter(prefix="/admin/config", tags=["config-admin"], dependencies=[Depends(verify_internal_api_key)])

Session = Annotated[AsyncSession, Depends(get_db)]
Actor = Annotated[str, Header(alias="X-Actor-Hash", min_length=64, max_length=64, pattern=r"^[0-9a-f]{64}$")]
Limit = Annotated[int, Query(ge=1, le=200)]
Offset = Annotated[int, Query(ge=0)]


@router.get("/documents")
async def list_documents(session: Session) -> dict:
    return {"items": await docs.overview(session)}


@router.get("/documents/{name}")
async def get_active_document(name: str, session: Session) -> dict:
    """Bản đang chạy (từ DB, hoặc bản mặc định trong image nếu chưa ai sửa), kèm số phiên bản để gửi lại ở ``expected_active_version`` khi kích hoạt."""
    return await docs.active_document(session, name)


@router.get("/documents/{name}/default")
async def get_default_document(name: str) -> dict:
    """Bản mặc định trong image: để so sánh hoặc đưa về nguyên trạng ban đầu (tải nó lên thành bản nháp rồi kích hoạt)."""
    return {"name": name, "content": docs.spec_of(name).default()}


@router.get("/documents/{name}/versions")
async def list_versions(name: str, session: Session, limit: Limit = 50, offset: Offset = 0) -> dict:
    return await docs.list_versions(session, name, limit=limit, offset=offset)


@router.get("/documents/{name}/versions/{version}")
async def get_version(name: str, version: int, session: Session) -> dict:
    row = await docs.get_version(session, name, version)
    return {**docs._meta(row), "content": row.content}


@router.post("/documents/{name}/drafts", status_code=status.HTTP_201_CREATED)
async def create_draft(name: str, body: DraftCreate, session: Session, actor: Actor) -> dict:
    """Lưu bản nháp (chưa ảnh hưởng gì). Bị từ chối 422 nếu thiếu khóa mà code dùng, sai kiểu, hoặc regex không biên dịch được."""
    return await docs.create_draft(session, name, body.content, note=body.note, actor=actor)


@router.post("/documents/{name}/versions/{version}/preview")
async def preview_version(name: str, version: int, session: Session) -> dict:
    """Khác biệt so với bản đang chạy, và (nếu tài liệu có cổng đo) kết quả chạy thử trên bộ câu có đáp án. Không ghi gì."""
    return await docs.preview_version(session, name, version)


@router.post("/documents/{name}/versions/{version}/activate")
async def activate_version(name: str, version: int, body: ActivateBody, session: Session, actor: Actor) -> dict:
    return await docs.activate(session, name, version, actor=actor, expected_active=body.expected_active_version, force=body.force)
