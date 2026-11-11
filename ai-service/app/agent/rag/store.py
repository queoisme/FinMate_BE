"""Ghi và đổi trạng thái tài liệu tri thức. Luồng duy nhất: Draft → Approved → Retired (hoặc Draft → Retired khi từ chối). Đổi sang Approved/Retired làm đổi nội dung được truy xuất, nên GỌI ``cache.bump_version()``
để mọi kết quả đệm cũ mất hiệu lực. Truy vấn dùng SQL thô vì cột ``vector`` và ``tsvector`` không có kiểu ORM tiện dụng."""

from __future__ import annotations

import uuid
from datetime import datetime

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

from app.agent.rag.cache import KbCache
from app.db.vector import to_literal

TRANSITIONS = {("Draft", "Approved"), ("Approved", "Retired"), ("Draft", "Retired")}


class KbError(Exception):
    """``code`` cố định: ``kb_document_not_found``, ``kb_invalid_transition``, ``kb_chunk_mismatch``."""

    def __init__(self, code: str) -> None:
        super().__init__(code)
        self.code = code


async def add_document(
    session: AsyncSession,
    *,
    title: str,
    source_name: str,
    source_url: str | None,
    as_of: datetime,
    effective_from: datetime,
    effective_to: datetime | None,
    chunks: list[str],
    embeddings: list[list[float]],
) -> uuid.UUID:
    """Tạo tài liệu ở trạng thái Draft cùng các đoạn đã nhúng. Một giao dịch: hoặc có đủ, hoặc không có gì."""
    if not chunks or len(chunks) != len(embeddings):
        raise KbError("kb_chunk_mismatch")
    document_id = (
        await session.execute(
            text(
                "INSERT INTO agent_kb_documents (title, source_name, source_url, as_of, effective_from, effective_to)"
                " VALUES (:title, :source_name, :source_url, :as_of, :effective_from, :effective_to) RETURNING id"
            ),
            {"title": title, "source_name": source_name, "source_url": source_url, "as_of": as_of, "effective_from": effective_from, "effective_to": effective_to},
        )
    ).scalar_one()
    for index, (chunk_text, vector) in enumerate(zip(chunks, embeddings)):
        await session.execute(
            text("INSERT INTO agent_kb_chunks (document_id, chunk_index, text, embedding) VALUES (:d, :i, :t, CAST(:v AS vector))"),
            {"d": document_id, "i": index, "t": chunk_text, "v": to_literal(vector)},
        )
    await session.commit()
    return document_id


async def set_status(session: AsyncSession, document_id: uuid.UUID, new_status: str, cache: KbCache | None = None) -> None:
    current = (await session.execute(text("SELECT status FROM agent_kb_documents WHERE id = :id FOR UPDATE"), {"id": document_id})).scalar_one_or_none()
    if current is None:
        raise KbError("kb_document_not_found")
    if (current, new_status) not in TRANSITIONS:
        raise KbError("kb_invalid_transition")
    await session.execute(
        text("UPDATE agent_kb_documents SET status = :s, version = version + 1, updated_at = now() WHERE id = :id"),
        {"s": new_status, "id": document_id},
    )
    await session.commit()
    if cache is not None and new_status in ("Approved", "Retired"):
        await cache.bump_version()  # sau commit: lượt truy xuất kế tiếp thấy dữ liệu mới, không đệm lại bản cũ


async def list_documents(session: AsyncSession, *, status: str | None, search: str | None, limit: int, offset: int) -> tuple[int, list[dict]]:
    where, params = ["1=1"], {"limit": limit, "offset": offset}
    if status:
        where.append("status = :status")
        params["status"] = status
    if search:
        where.append("(title ILIKE :q OR source_name ILIKE :q)")
        params["q"] = f"%{search}%"
    clause = " AND ".join(where)
    total = (await session.execute(text(f"SELECT count(*) FROM agent_kb_documents WHERE {clause}"), params)).scalar_one()
    rows = (
        await session.execute(
            text(
                "SELECT id, title, source_name, source_url, as_of, effective_from, effective_to, status, version,"
                f" (SELECT count(*) FROM agent_kb_chunks c WHERE c.document_id = agent_kb_documents.id) AS chunks FROM agent_kb_documents WHERE {clause}"
                " ORDER BY updated_at DESC LIMIT :limit OFFSET :offset"
            ),
            params,
        )
    ).mappings().all()
    return total, [dict(r) for r in rows]


async def get_document(session: AsyncSession, document_id: uuid.UUID) -> dict | None:
    """Một tài liệu kèm TOÀN BỘ nội dung (các đoạn ghép lại theo thứ tự), để người duyệt đọc trước khi bấm duyệt. None nếu không có."""
    row = (
        await session.execute(
            text(
                "SELECT id, title, source_name, source_url, as_of, effective_from, effective_to, status, version,"
                " (SELECT count(*) FROM agent_kb_chunks c WHERE c.document_id = d.id) AS chunks,"
                " (SELECT string_agg(c.text, ' ' ORDER BY c.chunk_index) FROM agent_kb_chunks c WHERE c.document_id = d.id) AS text"
                " FROM agent_kb_documents d WHERE d.id = :id"
            ),
            {"id": document_id},
        )
    ).mappings().first()
    return dict(row) if row else None
