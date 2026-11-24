"""Câu hỏi tri thức mà agent KHÔNG trả lời được (không có bằng chứng): ghi lại để nhân sự nội dung biết kho còn thiếu gì.

Chỉ lưu MÃ BĂM của câu hỏi (SHA-256 của bản chuẩn hóa), CHỦ ĐỀ của nó (mã trong ``agent_kb_topics``, do admin định nghĩa) và số lần gặp, KHÔNG lưu nội dung câu hỏi, không có mã người dùng.
Chủ đề là thứ giúp admin biết cần viết tài liệu về mảng nào mà không phải đọc câu hỏi của ai. Ứng viên không bao giờ tự vào kho: không có hàm nào ở đây tạo ``agent_kb_documents``;
tri thức mới chỉ vào kho qua luồng Draft → Approved do người duyệt (``store``)."""

from __future__ import annotations

import hashlib
import re
import unicodedata
import uuid

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

STATUSES = ("New", "Dismissed", "Covered")
_PUNCTUATION = re.compile(r"[^\w\s/]", re.UNICODE)


def question_hash(question: str) -> str:
    """Chuẩn hóa nhẹ (chữ thường, bỏ dấu câu, gộp khoảng trắng) để cùng một câu hỏi viết hơi khác nhau dồn vào một dòng."""
    normalized = " ".join(_PUNCTUATION.sub(" ", question.lower()).split())
    return hashlib.sha256(normalized.encode()).hexdigest()


def fold(text_: str) -> str:
    """Chữ thường, bỏ dấu (đ → d), bỏ dấu câu, gộp khoảng trắng: để khớp từ khóa chủ đề dù người dùng gõ có dấu hay không."""
    decomposed = unicodedata.normalize("NFD", text_.lower().replace("đ", "d"))
    stripped = "".join(c for c in decomposed if unicodedata.category(c) != "Mn")
    return " ".join(_PUNCTUATION.sub(" ", stripped).split())


def match_topic(question: str, topics: list[tuple[str, list[str]]]) -> str | None:
    """Mã chủ đề có nhiều từ khóa khớp nhất (nguyên cụm từ); hòa thì chủ đề đứng trước (``topics`` đã sắp theo thứ tự ưu tiên). Không khớp chủ đề nào: None."""
    folded = f" {fold(question)} "
    best: tuple[int, str] | None = None
    for code, keywords in topics:
        hits = sum(1 for k in keywords if k and f" {k} " in folded)
        if hits and (best is None or hits > best[0]):
            best = (hits, code)
    return best[1] if best else None


async def _active_topics(session: AsyncSession) -> list[tuple[str, list[str]]]:
    rows = (await session.execute(text("SELECT code, keywords FROM agent_kb_topics WHERE is_active ORDER BY sort_order, code"))).all()
    return [(r[0], list(r[1] or [])) for r in rows]


async def record(session: AsyncSession, question: str) -> None:
    topic = match_topic(question, await _active_topics(session))
    await session.execute(
        text(
            "INSERT INTO agent_kb_candidates (question_hash, topic_code) VALUES (:h, :t)"
            " ON CONFLICT (question_hash) DO UPDATE SET count = agent_kb_candidates.count + 1, last_seen = now(),"
            " topic_code = COALESCE(agent_kb_candidates.topic_code, EXCLUDED.topic_code)"
        ),
        {"h": question_hash(question), "t": topic},
    )
    await session.commit()


async def list_candidates(
    session: AsyncSession, *, status: str | None, limit: int, offset: int, topic: str | None = None
) -> tuple[int, list[dict]]:
    """``topic`` = mã chủ đề, hoặc ``"none"`` để lấy các câu chưa khớp chủ đề nào."""
    params: dict = {"limit": limit, "offset": offset}
    clauses = ["1=1"]
    if status:
        clauses.append("c.status = :status")
        params["status"] = status
    if topic == "none":
        clauses.append("c.topic_code IS NULL")
    elif topic:
        clauses.append("c.topic_code = :topic")
        params["topic"] = topic
    where = " AND ".join(clauses)
    total = (await session.execute(text(f"SELECT count(*) FROM agent_kb_candidates c WHERE {where}"), params)).scalar_one()
    rows = (
        await session.execute(
            text(
                "SELECT c.id, c.question_hash, c.count, c.first_seen, c.last_seen, c.status, c.topic_code, t.label AS topic_label"
                f" FROM agent_kb_candidates c LEFT JOIN agent_kb_topics t ON t.code = c.topic_code WHERE {where}"
                " ORDER BY c.count DESC, c.last_seen DESC LIMIT :limit OFFSET :offset"
            ),
            params,
        )
    ).mappings().all()
    return total, [dict(r) for r in rows]


async def topic_summary(session: AsyncSession, *, status: str | None = "New") -> list[dict]:
    """Mỗi chủ đề: số câu hỏi khác nhau và tổng số lần gặp, nhiều lần nhất trước; dòng ``topic_code = None`` là các câu chưa khớp chủ đề nào."""
    params: dict = {}
    where = "1=1"
    if status:
        where, params["status"] = "c.status = :status", status
    rows = (
        await session.execute(
            text(
                "SELECT c.topic_code, t.label AS topic_label, count(*) AS questions, sum(c.count)::int AS occurrences"
                f" FROM agent_kb_candidates c LEFT JOIN agent_kb_topics t ON t.code = c.topic_code WHERE {where}"
                " GROUP BY c.topic_code, t.label ORDER BY occurrences DESC, questions DESC"
            ),
            params,
        )
    ).mappings().all()
    return [dict(r) for r in rows]


async def set_status(session: AsyncSession, candidate_id: uuid.UUID, status: str) -> bool:
    """False nếu không có ứng viên. Chỉ đánh dấu đã xử lý (Dismissed/Covered); không đụng tới kho."""
    result = await session.execute(text("UPDATE agent_kb_candidates SET status = :s WHERE id = :id"), {"s": status, "id": candidate_id})
    await session.commit()
    return result.rowcount == 1
