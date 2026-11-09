"""Truy xuất tri thức: lai giữa vector (``<=>`` khoảng cách cosine) và full-text ('simple'), gộp bằng reciprocal rank fusion ``1/(60+rank)``.

Vì sao lai: vector bắt được ý ("tiết kiệm bao nhiêu mỗi tháng"), còn từ khóa đúng chữ như "50/30/20" thì full-text chắc hơn dù vector xếp thấp. Chỉ tài liệu ``Approved`` và còn hiệu lực
(``effective_from <= now < effective_to``) được trả; mỗi kết quả là ``Evidence`` nguồn ``document`` kèm tên nguồn và ngày cập nhật. Đệm theo (câu truy vấn, phiên bản kho): xem ``cache``.
Một kết quả đệm có thể sống thêm tối đa 1 giờ sau khi tài liệu hết hiệu lực theo ngày (duyệt/gỡ thì hiệu lực ngay vì đổi phiên bản)."""

from __future__ import annotations

import uuid
from collections.abc import Awaitable, Callable
from datetime import datetime

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

from app.agent.rag.cache import KbCache, query_hash
from app.agent.rag.embedder import embed
from app.agent.schemas import Evidence
from app.core.config import settings
from app.db.vector import to_literal

RRF_K = 60
CANDIDATES = 20
_ELIGIBLE = "d.status = 'Approved' AND d.effective_from <= :now AND (d.effective_to IS NULL OR d.effective_to > :now)"
_VECTOR_SQL = (
    f"SELECT c.id FROM agent_kb_chunks c JOIN agent_kb_documents d ON d.id = c.document_id WHERE {_ELIGIBLE}"
    f" ORDER BY c.embedding <=> CAST(:q AS vector) LIMIT {CANDIDATES}"
)
# Từ khóa của câu truy vấn lấy bằng CHÍNH bộ tách từ của Postgres ('simple'), nối bằng OR: "50/30/20" là một từ vựng nguyên vẹn ở cả hai phía.
_TEXT_SQL = (
    "WITH q AS (SELECT to_tsquery('simple', (SELECT string_agg(quote_literal(l), ' | ') FROM unnest(tsvector_to_array(to_tsvector('simple', :t))) AS l)) AS tq)"
    f" SELECT c.id FROM agent_kb_chunks c JOIN agent_kb_documents d ON d.id = c.document_id, q WHERE {_ELIGIBLE} AND c.tsv @@ q.tq"
    f" ORDER BY ts_rank_cd(c.tsv, q.tq) DESC LIMIT {CANDIDATES}"
)
_FETCH_SQL = (
    "SELECT c.id, c.text, d.source_name, d.source_url, d.as_of FROM agent_kb_chunks c JOIN agent_kb_documents d ON d.id = c.document_id WHERE c.id = ANY(CAST(:ids AS uuid[]))"
)

Embedder = Callable[[list[str]], Awaitable[list[list[float]]]]


def rrf(rankings: list[list[uuid.UUID]], k: int = RRF_K) -> list[uuid.UUID]:
    """Gộp các bảng xếp hạng: điểm của một đoạn = Σ 1/(k + hạng) (hạng bắt đầu từ 1). Hòa điểm thì đoạn xuất hiện trước giữ thứ tự."""
    scores: dict[uuid.UUID, float] = {}
    for ranking in rankings:
        for rank, chunk_id in enumerate(ranking, start=1):
            scores[chunk_id] = scores.get(chunk_id, 0.0) + 1.0 / (k + rank)
    return sorted(scores, key=lambda c: -scores[c])


async def _search(session: AsyncSession, query: str, k: int, now: datetime, embedder: Embedder) -> list[dict]:
    (vector,) = await embedder([query])
    by_vector = (await session.execute(text(_VECTOR_SQL), {"q": to_literal(vector), "now": now})).scalars().all()
    by_text = (await session.execute(text(_TEXT_SQL), {"t": query, "now": now})).scalars().all()
    top = rrf([list(by_vector), list(by_text)])[:k]
    if not top:
        return []
    rows = {r["id"]: r for r in (await session.execute(text(_FETCH_SQL), {"ids": [str(i) for i in top]})).mappings().all()}
    return [
        {
            "id": f"kb:{chunk_id.hex[:16]}",
            "key": "knowledge",
            "text": rows[chunk_id]["text"],
            "unit": "text",
            "source": "document",
            "as_of": rows[chunk_id]["as_of"].isoformat(),
            "confidence": 1.0,  # tài liệu đã duyệt và đang hiệu lực: đầy đủ và còn mới theo định nghĩa của kho (độ tin cậy ở đây không phải xác suất)
            "source_name": rows[chunk_id]["source_name"],
            "source_url": rows[chunk_id]["source_url"],
        }
        for chunk_id in top
        if chunk_id in rows
    ]


async def retrieve(
    session: AsyncSession,
    query: str,
    k: int,
    now: datetime,
    *,
    cache: KbCache | None = None,
    embedder: Embedder | None = None,
    enabled: bool | None = None,
) -> list[Evidence]:
    """Top-``k`` đoạn tri thức liên quan. ``enabled`` mặc định lấy ``AGENT_RAG_ENABLED``; tắt thì trả rỗng (đánh giá cắt bỏ)."""
    if not (settings.agent_rag_enabled if enabled is None else enabled) or not query.strip():
        return []
    embed_query = embedder or (lambda texts: embed(texts, kind="query"))

    async def load() -> list[dict]:
        return await _search(session, query, k, now, embed_query)

    if cache is None:
        rows = await load()
    else:
        rows = await cache.get_or_set(query_hash(query, k), await cache.version(), load)
    return [Evidence.model_validate(r) for r in rows]
