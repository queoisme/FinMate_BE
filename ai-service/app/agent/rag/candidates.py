"""Câu hỏi tri thức mà agent KHÔNG trả lời được (không có bằng chứng): ghi lại để nhân sự nội dung biết kho còn thiếu gì.

Chỉ lưu MÃ BĂM của câu hỏi (SHA-256 của bản chuẩn hóa) và số lần gặp, KHÔNG lưu nội dung câu hỏi, không có mã người dùng. Ứng viên không bao giờ tự vào kho: không có hàm nào ở đây tạo ``agent_kb_documents``;
tri thức mới chỉ vào kho qua luồng Draft → Approved do người duyệt (``store``)."""

from __future__ import annotations

import hashlib
import re
import uuid

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

STATUSES = ("New", "Dismissed", "Covered")
_PUNCTUATION = re.compile(r"[^\w\s/]", re.UNICODE)


def question_hash(question: str) -> str:
    """Chuẩn hóa nhẹ (chữ thường, bỏ dấu câu, gộp khoảng trắng) để cùng một câu hỏi viết hơi khác nhau dồn vào một dòng."""
    normalized = " ".join(_PUNCTUATION.sub(" ", question.lower()).split())
    return hashlib.sha256(normalized.encode()).hexdigest()


async def record(session: AsyncSession, question: str) -> None:
    await session.execute(
        text(
            "INSERT INTO agent_kb_candidates (question_hash) VALUES (:h)"
            " ON CONFLICT (question_hash) DO UPDATE SET count = agent_kb_candidates.count + 1, last_seen = now()"
        ),
        {"h": question_hash(question)},
    )
    await session.commit()


async def list_candidates(session: AsyncSession, *, status: str | None, limit: int, offset: int) -> tuple[int, list[dict]]:
    params: dict = {"limit": limit, "offset": offset}
    where = "1=1"
    if status:
        where, params["status"] = "status = :status", status
    total = (await session.execute(text(f"SELECT count(*) FROM agent_kb_candidates WHERE {where}"), params)).scalar_one()
    rows = (
        await session.execute(
            text(f"SELECT id, question_hash, count, first_seen, last_seen, status FROM agent_kb_candidates WHERE {where} ORDER BY count DESC, last_seen DESC LIMIT :limit OFFSET :offset"),
            params,
        )
    ).mappings().all()
    return total, [dict(r) for r in rows]


async def set_status(session: AsyncSession, candidate_id: uuid.UUID, status: str) -> bool:
    """False nếu không có ứng viên. Chỉ đánh dấu đã xử lý (Dismissed/Covered); không đụng tới kho."""
    result = await session.execute(text("UPDATE agent_kb_candidates SET status = :s WHERE id = :id"), {"s": status, "id": candidate_id})
    await session.commit()
    return result.rowcount == 1
