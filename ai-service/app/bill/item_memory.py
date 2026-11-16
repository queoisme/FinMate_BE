"""Bộ nhớ danh mục của TỪNG MÓN theo người dùng (``user_item_memory``): món người dùng đã sửa (``corrected``) hay giữ nguyên gợi ý AI (``accepted``).

Khác ``kb_item_categories`` (dùng chung, theo slug): ở đây khóa là ``(user_id_hash, item_key)`` và giá trị là id UUID danh mục của backend.
Tên món là chữ của người dùng nên qua cùng bộ lọc với bảng nhớ chung (``usable_item_key``) và bị bỏ nếu còn chuỗi số dài. Không log tên món.

Quy tắc ghi: ``corrected`` luôn đè; ``accepted`` chỉ ghi khi chưa có dòng hoặc dòng cũ cũng là ``accepted``. Mỗi người tối đa ``MAX_ROWS_PER_USER`` dòng.
"""

from __future__ import annotations

import uuid
from typing import Literal

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

from app.bill.item_classifier import usable_item_key
from app.bill.knowledge import MemoryHit
from app.bill.samples import has_long_digit_run

Kind = Literal["corrected", "accepted"]
MAX_ROWS_PER_USER = 1000

_UPSERT_SQL = text(
    "INSERT INTO user_item_memory (user_id_hash, item_key, category_id, source) VALUES (:user, :key, :category, :source) "
    "ON CONFLICT (user_id_hash, item_key) DO UPDATE SET category_id = EXCLUDED.category_id, source = EXCLUDED.source, updated_at = now() "
    "WHERE EXCLUDED.source = 'corrected' OR user_item_memory.source = 'accepted'"
)

# Giữ MAX dòng đầu theo thứ tự: corrected trước, rồi mới nhất trước; phần còn lại (accepted cũ nhất, rồi corrected cũ nhất) bị xoá.
_TRIM_SQL = text(
    "DELETE FROM user_item_memory WHERE user_id_hash = :user AND item_key IN ("
    "SELECT item_key FROM user_item_memory WHERE user_id_hash = :user "
    "ORDER BY (source = 'corrected') DESC, updated_at DESC, item_key OFFSET :cap)"
)


async def lookup(session: AsyncSession, user_id_hash: str, keys: list[str]) -> dict[str, MemoryHit]:
    """``khóa món -> (id danh mục, nguồn)`` cho những khóa người dùng này đã có; khóa chưa có thì vắng mặt."""
    if not keys:
        return {}
    rows = await session.execute(
        text("SELECT item_key, category_id, source FROM user_item_memory WHERE user_id_hash = :user AND item_key = ANY(:keys)"),
        {"user": user_id_hash, "keys": list(keys)},
    )
    return {key: MemoryHit(category, source) for key, category, source in rows.all()}


async def record(session: AsyncSession, user_id_hash: str, entries: list[tuple[str, uuid.UUID, Kind]]) -> int:
    """Ghi ``(tên món, id danh mục, kind)`` lần lượt từng mục. Trả số dòng thực sự được ghi (``accepted`` không đè ``corrected`` thì không tính).

    Tên không dùng được hoặc còn chuỗi số dài thì bỏ qua (không phải lỗi). Ghi xong, vượt ``MAX_ROWS_PER_USER`` thì xoá bớt.
    """
    written = 0
    for name, category_id, kind in entries:
        key = usable_item_key(name)
        if key is None or has_long_digit_run([name]):
            continue
        result = await session.execute(_UPSERT_SQL, {"user": user_id_hash, "key": key, "category": category_id, "source": kind})
        written += result.rowcount or 0
    # ponytail: trần cứng, nâng khi có người chạm
    await session.execute(_TRIM_SQL, {"user": user_id_hash, "cap": MAX_ROWS_PER_USER})
    await session.commit()
    return written


async def delete_by_user_hash(session: AsyncSession, user_id_hash: str) -> int:
    """Xoá mọi món đã nhớ của một người dùng (xoá tài khoản). Trả số dòng đã xoá."""
    result = await session.execute(text("DELETE FROM user_item_memory WHERE user_id_hash = :user"), {"user": user_id_hash})
    await session.commit()
    return result.rowcount or 0
