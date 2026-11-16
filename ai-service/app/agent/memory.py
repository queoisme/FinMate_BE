"""Bộ nhớ dài hạn của AI Financial Agent: sở thích NGẮN của người dùng (≤200 ký tự, không phải chỗ chứa dữ liệu tự do), lưu theo ``user_id_hash``.

Người dùng xem, sửa, xóa được qua backend (danh sách khóa cho phép do backend giữ); ai-service chỉ giữ dạng khóa, độ dài và cô lập theo hash. Nội dung KHÔNG bao giờ được log. Khi đưa vào lời nhắc,
bộ nhớ là DỮ LIỆU (xem ``planner.py``), không phải chỉ dẫn: một giá trị như "bỏ qua mọi quy tắc" không có tác dụng gì."""

from __future__ import annotations

import re

from sqlalchemy import delete, func, select
from sqlalchemy.dialects.postgresql import insert
from sqlalchemy.ext.asyncio import AsyncSession

from app.db.models.agent import AgentUserMemory

KEY_PATTERN = re.compile(r"^[a-z][a-z0-9_]{2,47}$")
MAX_VALUE_LENGTH = 200


async def list_memory(session: AsyncSession, user_hash: str) -> list[AgentUserMemory]:
    rows = await session.execute(select(AgentUserMemory).where(AgentUserMemory.user_id_hash == user_hash).order_by(AgentUserMemory.key))
    return list(rows.scalars())


async def put_memory(session: AsyncSession, user_hash: str, key: str, value: str) -> None:
    """Ghi hoặc thay giá trị của một khóa (mỗi (người dùng, khóa) đúng một dòng)."""
    statement = insert(AgentUserMemory).values(user_id_hash=user_hash, key=key, value=value)
    await session.execute(statement.on_conflict_do_update(index_elements=["user_id_hash", "key"], set_={"value": statement.excluded.value, "updated_at": func.now()}))
    await session.commit()


async def delete_memory(session: AsyncSession, user_hash: str, key: str | None = None) -> int:
    """Xóa một khóa (hoặc mọi khóa nếu ``key`` là None) của MỘT người dùng; trả số dòng đã xóa. Không có thì trả 0, không phải lỗi."""
    statement = delete(AgentUserMemory).where(AgentUserMemory.user_id_hash == user_hash)
    if key is not None:
        statement = statement.where(AgentUserMemory.key == key)
    result = await session.execute(statement)
    await session.commit()
    return result.rowcount
