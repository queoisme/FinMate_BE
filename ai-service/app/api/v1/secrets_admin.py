"""API quản trị KEY BÊN THỨ BA (nội bộ): chỉ backend gọi, sau khi đã xác thực admin. Người thực hiện đi kèm ở ``X-Actor-Hash`` (SHA-256 id admin).

KHÔNG endpoint nào trả giá trị key, kể cả cho admin: chỉ trạng thái (nguồn, 4 ký tự cuối, lúc đổi). Thân ``PUT`` cố ý không khai báo ràng buộc kiểu pydantic cho ``value``: lỗi kiểm tra của
pydantic chép lại giá trị đầu vào vào phản hồi, còn ``secret_store`` tự kiểm tra và trả thông điệp không nhắc lại giá trị.

Lỗi là JSON ``{"error": {"code", "message"}}`` cùng mã HTTP (404/422), giống các API quản trị khác."""

from typing import Annotated

from fastapi import APIRouter, Depends, Header, Query
from pydantic import BaseModel
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.deps import get_db, verify_internal_api_key
from app.services import secret_store

router = APIRouter(prefix="/admin/secrets", tags=["secrets-admin"], dependencies=[Depends(verify_internal_api_key)])

Session = Annotated[AsyncSession, Depends(get_db)]
Actor = Annotated[str, Header(alias="X-Actor-Hash", min_length=64, max_length=64, pattern=r"^[0-9a-f]{64}$")]
Reason = Annotated[str | None, Query(max_length=500)]


class PutBody(BaseModel):
    value: object = None
    reason: str | None = None


@router.get("")
async def list_secrets(session: Session) -> dict:
    """Từng key: nguồn (``db`` nhập qua trang quản trị, ``env`` biến môi trường, ``none`` chưa có), 4 ký tự cuối, lúc đổi. ``available`` là đã có khóa chủ để lưu key chưa."""
    return await secret_store.status(session)


@router.put("/{name}")
async def put_secret(name: str, body: PutBody, session: Session, actor: Actor) -> dict:
    """Ghi đè key (mã hóa trước khi lưu). Thân: ``{"value": "...", "reason": "..."}``. Trả trạng thái mới, không trả giá trị."""
    await secret_store.save(session, name, body.value, actor, (body.reason or None) and body.reason[:500])
    return await _item(session, name)


@router.delete("/{name}")
async def delete_secret(name: str, session: Session, actor: Actor, reason: Reason = None) -> dict:
    """Xóa key trong DB: dịch vụ quay về key ở biến môi trường nếu có."""
    await secret_store.delete(session, name, actor, reason)
    return await _item(session, name)


@router.post("/{name}/test")
async def test_secret(name: str) -> dict:
    """Thử key đang dùng bằng một lệnh gọi chỉ-đọc tới nhà cung cấp: ``{"ok": bool, "detail": "..."}``. Không ghi gì."""
    return await secret_store.test(name)


async def _item(session: AsyncSession, name: str) -> dict:
    return next(item for item in (await secret_store.status(session))["items"] if item["name"] == name)
