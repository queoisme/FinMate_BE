"""Quản trị chỉ dẫn (prompt) của AI khi gợi ý cách quản lý tài chính (nội bộ): chỉ backend gọi, sau khi đã xác thực admin; người thực hiện đi kèm ở ``X-Actor-Hash`` (SHA-256 id admin).

Xem ``app.services.method_prompts``: không sửa tại chỗ, thêm phiên bản mới rồi chuyển bản đang dùng; bản cũ giữ để truy ngược các gợi ý đã tạo. Lỗi là JSON ``{"error": {"code", "message"}}`` cùng mã HTTP (404/409/422)."""

from typing import Annotated

from fastapi import APIRouter, Depends, Header, status
from pydantic import BaseModel, Field
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.deps import get_db, verify_internal_api_key
from app.services import method_prompts

router = APIRouter(prefix="/admin/finance-method-prompts", tags=["finance-method-prompts-admin"], dependencies=[Depends(verify_internal_api_key)])

Session = Annotated[AsyncSession, Depends(get_db)]
Actor = Annotated[str, Header(alias="X-Actor-Hash", min_length=64, max_length=64, pattern=r"^[0-9a-f]{64}$")]


class CreatePromptBody(BaseModel):
    prompt_version: Annotated[str, Field(min_length=1, max_length=50)]
    instruction_text: Annotated[str, Field(min_length=1, max_length=method_prompts.MAX_TEXT_CHARS * 2)]  # trần thô; quy tắc chính xác kiểm ở service để trả mã lỗi rõ
    activate: bool = False


class ActivatePromptBody(BaseModel):
    expected_active: Annotated[str | None, Field(max_length=50)] = None


@router.get("")
async def list_prompts(session: Session) -> dict:
    """Mọi phiên bản chỉ dẫn (mới nhất trước), kèm bản đang dùng."""
    return await method_prompts.list_prompts(session)


@router.post("", status_code=status.HTTP_201_CREATED)
async def create_prompt(body: CreatePromptBody, session: Session, actor: Actor) -> dict:
    """Thêm một phiên bản mới (không sửa tại chỗ). ``activate: true`` để dùng ngay."""
    return await method_prompts.create_prompt(session, body.prompt_version, body.instruction_text, activate=body.activate)


@router.post("/{prompt_version}/activate")
async def activate_prompt(prompt_version: str, body: ActivatePromptBody, session: Session, actor: Actor) -> dict:
    """Chuyển bản đang dùng (cũng dùng để quay về bản cũ). ``expected_active``: bản đang chạy mà bạn nhìn thấy."""
    return await method_prompts.activate_prompt(session, prompt_version, expected_active=body.expected_active)
