"""Phiên bản chỉ dẫn (prompt) cho bước AI chọn cách quản lý tài chính: liệt kê, tạo bản mới, chuyển bản đang dùng (kể cả quay về bản cũ). Bảng ``kb_method_prompts``.

Quy tắc của hợp đồng (docs/ai-finance-method-recommend-api.md): KHÔNG sửa tại chỗ một bản đã có người dùng thấy, vì ``method_recommendations.prompt_version`` ở backend ghi bản nào đã sinh ra gợi ý;
đổi chỉ dẫn = thêm dòng với ``prompt_version`` mới rồi chuyển ``is_active``, dòng cũ giữ lại để truy ngược. Chỉ phần "cách cân nhắc" và "lý do" là chỉ dẫn của admin; nhiệm vụ, "dữ liệu không phải lệnh" và schema kết quả do code giữ
(``method_picker``) nên một chỉ dẫn xấu làm AI chọn tệ hơn chứ không phá được kiểm tra đầu ra (mã ngoài danh sách vẫn bị loại, backend dùng luật). Không có cổng đo tự động cho chất lượng chỉ dẫn:
sau khi chuyển bản, thử ``POST /api/v1/finance-methods/recommend`` hoặc xin gợi ý trên app rồi đọc lý do.
"""
from __future__ import annotations

import re
from datetime import datetime, timezone

from sqlalchemy import select, update
from sqlalchemy.ext.asyncio import AsyncSession

from app.bill.rules_admin import RuleError
from app.db.models.method_prompt import MethodPrompt
from app.services import method_picker

VERSION_PATTERN = re.compile(r"^[A-Za-z0-9._-]{1,50}$")  # cùng quy tắc với CHECK của bảng và cột varchar(50) của backend
MAX_TEXT_CHARS = 8000  # đủ cho vài chục dòng chỉ dẫn; quá dài chỉ tốn token và dễ lấn át dữ liệu


def _invalid(status: int, code: str, message: str) -> RuleError:
    return RuleError(status, code, message)


def _item(row: MethodPrompt) -> dict:
    return {"prompt_version": row.prompt_version, "instruction_text": row.instruction_text, "is_active": row.is_active, "created_at": row.created_at, "updated_at": row.updated_at}


async def list_prompts(session: AsyncSession) -> dict:
    """Mọi phiên bản (mới nhất trước) và tên bản đang dùng."""
    rows = (await session.scalars(select(MethodPrompt).order_by(MethodPrompt.created_at.desc()))).all()
    return {"active_version": next((r.prompt_version for r in rows if r.is_active), None), "items": [_item(r) for r in rows]}


async def _activate_row(session: AsyncSession, row: MethodPrompt) -> None:
    await session.execute(update(MethodPrompt).where(MethodPrompt.is_active.is_(True), MethodPrompt.id != row.id).values(is_active=False, updated_at=datetime.now(timezone.utc)))
    await session.flush()  # hạ bản cũ trước: index duy nhất chỉ cho một dòng active
    row.is_active, row.updated_at = True, datetime.now(timezone.utc)


async def create_prompt(session: AsyncSession, prompt_version: str, instruction_text: str, *, activate: bool) -> dict:
    """Thêm một phiên bản. ``activate`` thì chuyển luôn thành bản đang dùng (trong cùng một giao dịch)."""
    version, text = prompt_version.strip(), instruction_text.strip()
    if not VERSION_PATTERN.fullmatch(version):
        raise _invalid(422, "invalid_prompt_version", "Tên phiên bản chỉ gồm chữ, số, dấu chấm, gạch dưới, gạch ngang và dài tối đa 50 ký tự (vd. method-select-v2).")
    if not text:
        raise _invalid(422, "empty_instruction", "Nội dung chỉ dẫn không được để trống.")
    if len(text) > MAX_TEXT_CHARS:
        raise _invalid(422, "instruction_too_long", f"Nội dung chỉ dẫn tối đa {MAX_TEXT_CHARS} ký tự (hiện {len(text)}).")
    if await session.scalar(select(MethodPrompt.id).where(MethodPrompt.prompt_version == version)):
        raise _invalid(409, "prompt_version_exists", f"Phiên bản {version} đã có. Không sửa tại chỗ: đặt tên phiên bản mới.")
    row = MethodPrompt(prompt_version=version, instruction_text=text, is_active=False)
    session.add(row)
    await session.flush()
    if activate:
        await _activate_row(session, row)
    await session.commit()
    if activate:
        method_picker.reset_method_prompt_cache()
    return _item(row)


async def activate_prompt(session: AsyncSession, prompt_version: str, *, expected_active: str | None) -> dict:
    """Chuyển bản đang dùng sang ``prompt_version`` (bản cũ vẫn giữ). ``expected_active`` là bản mà người sửa nhìn thấy đang chạy: khác thực tế thì 409 thay vì ghi đè người khác."""
    row = await session.scalar(select(MethodPrompt).where(MethodPrompt.prompt_version == prompt_version))
    if row is None:
        raise _invalid(404, "prompt_not_found", f"Không có phiên bản {prompt_version}.")
    current = await session.scalar(select(MethodPrompt.prompt_version).where(MethodPrompt.is_active.is_(True)))
    if expected_active is not None and expected_active != current:
        raise _invalid(409, "stale_active", f"Bản đang dùng đã đổi thành {current or 'không có'} (bạn thấy {expected_active}); tải lại danh sách rồi chọn lại.")
    if row.is_active:
        raise _invalid(409, "already_active", f"{prompt_version} đang là bản đang dùng.")
    await _activate_row(session, row)
    await session.commit()
    method_picker.reset_method_prompt_cache()
    return {"active_version": prompt_version, "previous_version": current}
