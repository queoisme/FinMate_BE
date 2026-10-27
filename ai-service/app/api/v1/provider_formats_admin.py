"""API quản trị định dạng thông báo theo nhà cung cấp (nội bộ): chỉ backend gọi, sau khi đã xác thực admin. Xác thực ở đây là khóa nội bộ; người thực hiện đi kèm ở
``X-Actor-Hash`` (SHA-256 id admin) và được ghi vào ``kb_audit_log``.

Luồng sửa một regex mà không đụng bản đang chạy: ``POST /clone`` (draft mới từ bản mới nhất của package) -> sửa pattern / trường / mẫu của draft -> ``POST /{id}/try`` thử một thông báo
-> ``GET /{id}/check`` -> ``POST /{id}/activate`` (chỉ thành công khi MỌI mẫu khớp; AI Service nạp lại trong vòng 60 giây). Sai thì ``POST /{id}/archive`` bản mới, bản cũ vẫn chạy.
Nhà cung cấp mới hoàn toàn: ``POST ""`` tạo draft trống rồi thêm pattern, trường, mẫu.

Lỗi là JSON ``{"error": {"code", "message", "details"?}}`` cùng mã HTTP (404/409/422), giống ``/admin/rules`` và ``/admin/config``."""

import uuid
from typing import Annotated

from fastapi import APIRouter, Depends, Header, Query, Response, status
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.deps import get_db, verify_internal_api_key
from app.schemas.provider_formats import (
    FieldCreate,
    FieldUpdate,
    FormatClone,
    FormatCreate,
    FormatUpdate,
    PatternCreate,
    PatternUpdate,
    Reasoned,
    SampleCreate,
    TryBody,
)
from app.services import provider_format_admin as admin

router = APIRouter(prefix="/admin/provider-formats", tags=["provider-formats-admin"], dependencies=[Depends(verify_internal_api_key)])

Session = Annotated[AsyncSession, Depends(get_db)]
Actor = Annotated[str, Header(alias="X-Actor-Hash", min_length=64, max_length=64, pattern=r"^[0-9a-f]{64}$")]
Limit = Annotated[int, Query(ge=1, le=200)]
Offset = Annotated[int, Query(ge=0)]


@router.get("")
async def list_formats(
    session: Session, package_name: str | None = None, status_filter: Annotated[str | None, Query(alias="status", pattern="^(draft|active|archived)$")] = None,
    limit: Limit = 50, offset: Offset = 0,
) -> dict:
    return await admin.list_formats(session, package_name=package_name, status=status_filter, limit=limit, offset=offset)


@router.post("", status_code=status.HTTP_201_CREATED)
async def create_format(body: FormatCreate, session: Session, actor: Actor) -> dict:
    """Draft TRỐNG cho một nhà cung cấp (phiên bản tiếp theo của package)."""
    result = await admin.create_blank(session, body, actor)
    await session.commit()
    return result


@router.post("/clone", status_code=status.HTTP_201_CREATED)
async def clone_format(body: FormatClone, session: Session, actor: Actor) -> dict:
    """Draft mới (phiên bản + 1) sao từ bản MỚI NHẤT của package, kèm pattern, trường và mẫu."""
    result = await admin.clone(session, body.package_name, actor, body.reason)
    await session.commit()
    return result


@router.get("/{format_id}")
async def get_format(format_id: uuid.UUID, session: Session) -> dict:
    return await admin.detail(session, format_id)


@router.patch("/{format_id}")
async def update_format(format_id: uuid.UUID, body: FormatUpdate, session: Session, actor: Actor) -> dict:
    result = await admin.update_format(session, format_id, body, actor)
    await session.commit()
    return result


@router.get("/{format_id}/check")
async def check_format(format_id: uuid.UUID, session: Session) -> dict:
    """Mọi lý do định dạng này CHƯA đủ điều kiện kích hoạt (``problems`` rỗng = đủ). Không đổi dữ liệu."""
    return await admin.check(session, format_id)


@router.post("/{format_id}/try")
async def try_text(format_id: uuid.UUID, body: TryBody, session: Session) -> dict:
    """Chạy thử một thông báo qua các pattern đang bật của định dạng (kể cả draft). Không ghi gì, không log nội dung."""
    return await admin.try_text(session, format_id, body.text)


@router.post("/{format_id}/activate")
async def activate_format(format_id: uuid.UUID, body: Reasoned, session: Session, actor: Actor) -> dict:
    """409 ``format_not_ready`` kèm ``details`` (từng lý do) khi chưa qua cổng kích hoạt."""
    result = await admin.activate(session, format_id, actor, body.reason)
    await session.commit()
    return result


@router.post("/{format_id}/archive")
async def archive_format(format_id: uuid.UUID, body: Reasoned, session: Session, actor: Actor) -> dict:
    result = await admin.archive(session, format_id, actor, body.reason)
    await session.commit()
    return result


@router.post("/{format_id}/patterns", status_code=status.HTTP_201_CREATED)
async def add_pattern(format_id: uuid.UUID, body: PatternCreate, session: Session, actor: Actor) -> dict:
    result = await admin.add_pattern(session, format_id, body, actor)
    await session.commit()
    return result


@router.patch("/{format_id}/patterns/{pattern_id}")
async def update_pattern(format_id: uuid.UUID, pattern_id: uuid.UUID, body: PatternUpdate, session: Session, actor: Actor) -> dict:
    result = await admin.update_pattern(session, format_id, pattern_id, body, actor)
    await session.commit()
    return result


@router.delete("/{format_id}/patterns/{pattern_id}", status_code=status.HTTP_204_NO_CONTENT)
async def delete_pattern(format_id: uuid.UUID, pattern_id: uuid.UUID, session: Session, actor: Actor) -> Response:
    await admin.delete_pattern(session, format_id, pattern_id, actor)
    await session.commit()
    return Response(status_code=status.HTTP_204_NO_CONTENT)


@router.post("/{format_id}/fields", status_code=status.HTTP_201_CREATED)
async def add_field(format_id: uuid.UUID, body: FieldCreate, session: Session, actor: Actor) -> dict:
    result = await admin.add_field(session, format_id, body, actor)
    await session.commit()
    return result


@router.patch("/{format_id}/fields/{field_id}")
async def update_field(format_id: uuid.UUID, field_id: uuid.UUID, body: FieldUpdate, session: Session, actor: Actor) -> dict:
    result = await admin.update_field(session, format_id, field_id, body, actor)
    await session.commit()
    return result


@router.delete("/{format_id}/fields/{field_id}", status_code=status.HTTP_204_NO_CONTENT)
async def delete_field(format_id: uuid.UUID, field_id: uuid.UUID, session: Session, actor: Actor) -> Response:
    await admin.delete_field(session, format_id, field_id, actor)
    await session.commit()
    return Response(status_code=status.HTTP_204_NO_CONTENT)


@router.post("/{format_id}/samples", status_code=status.HTTP_201_CREATED)
async def add_sample(format_id: uuid.UUID, body: SampleCreate, session: Session, actor: Actor) -> dict:
    result = await admin.add_sample(session, format_id, body, actor)
    await session.commit()
    return result


@router.delete("/{format_id}/samples/{sample_id}", status_code=status.HTTP_204_NO_CONTENT)
async def delete_sample(format_id: uuid.UUID, sample_id: uuid.UUID, session: Session, actor: Actor) -> Response:
    await admin.delete_sample(session, format_id, sample_id, actor)
    await session.commit()
    return Response(status_code=status.HTTP_204_NO_CONTENT)
