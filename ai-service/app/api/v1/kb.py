"""Kho tri thức của AI Financial Agent (``/api/v1/kb/*``): tạo tài liệu (luôn Draft), duyệt / gỡ, liệt kê, và tìm kiếm.

Backend gọi các route này (proxy quản trị ở Task 15); route không nhận ``user_id`` và không ghi nội dung truy vấn vào log. Lỗi nhúng vector → 503 với mã cố định (``embedding_*``)."""

from __future__ import annotations

import uuid
from datetime import UTC, datetime
from typing import Annotated

import structlog
from fastapi import APIRouter, Depends, HTTPException, Query
from pydantic import BaseModel, ConfigDict, Field
from sqlalchemy.ext.asyncio import AsyncSession

from app.agent.rag import store
from app.agent.rag.cache import KbCache
from app.agent.rag.chunker import chunk
from app.agent.rag.embedder import EmbeddingError, embed
from app.agent.rag.retriever import retrieve
from app.api.deps import get_db, verify_internal_api_key
from app.core.config import settings

logger = structlog.get_logger(__name__)
router = APIRouter(dependencies=[Depends(verify_internal_api_key)])

_cache: KbCache | None = None


def get_kb_cache() -> KbCache:
    """Đệm Redis dùng chung của tiến trình (dựng lần đầu khi cần). Test thay bằng bản giả qua ``dependency_overrides``."""
    global _cache
    if _cache is None:
        import redis.asyncio as redis

        _cache = KbCache(redis.from_url(settings.redis_url, decode_responses=True))
    return _cache


class DocumentCreate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    title: str = Field(min_length=1, max_length=200)
    source_name: str = Field(min_length=1, max_length=200)  # bắt buộc: không có nguồn thì không là bằng chứng
    source_url: str | None = Field(default=None, max_length=500)
    as_of: datetime
    effective_from: datetime
    effective_to: datetime | None = None
    text: str = Field(min_length=1, max_length=50_000)


class SearchRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    query: str = Field(min_length=1, max_length=500)
    k: int = Field(default=3, ge=1, le=10)


def _fail(error: store.KbError | EmbeddingError) -> HTTPException:
    status = {"kb_document_not_found": 404, "kb_invalid_transition": 409, "kb_chunk_mismatch": 422}.get(error.code, 503)
    return HTTPException(status_code=status, detail=error.code)


@router.post("/kb/documents", status_code=201)
async def create_document(body: DocumentCreate, session: Annotated[AsyncSession, Depends(get_db)]) -> dict:
    parts = chunk(body.text)
    try:
        vectors = await embed(parts, kind="document", title=body.title)
        document_id = await store.add_document(
            session, title=body.title, source_name=body.source_name, source_url=body.source_url, as_of=body.as_of,
            effective_from=body.effective_from, effective_to=body.effective_to, chunks=parts, embeddings=vectors,
        )
    except (EmbeddingError, store.KbError) as error:
        raise _fail(error) from error
    logger.info("kb_document_created", chunks=len(parts))
    return {"id": str(document_id), "status": "Draft", "chunks": len(parts)}


@router.get("/kb/documents")
async def list_documents(
    session: Annotated[AsyncSession, Depends(get_db)],
    status: str | None = Query(default=None, pattern="^(Draft|Approved|Retired)$"),
    search: str | None = Query(default=None, max_length=100),
    limit: int = Query(default=20, ge=1, le=100),
    offset: int = Query(default=0, ge=0),
) -> dict:
    total, items = await store.list_documents(session, status=status, search=search, limit=limit, offset=offset)
    return {"total": total, "items": [{**i, "id": str(i["id"])} for i in items]}


async def _transition(document_id: uuid.UUID, new_status: str, session: AsyncSession, cache: KbCache) -> dict:
    try:
        await store.set_status(session, document_id, new_status, cache)
    except store.KbError as error:
        raise _fail(error) from error
    logger.info("kb_document_status", status=new_status)
    return {"id": str(document_id), "status": new_status}


@router.post("/kb/documents/{document_id}/approve")
async def approve_document(document_id: uuid.UUID, session: Annotated[AsyncSession, Depends(get_db)], cache: Annotated[KbCache, Depends(get_kb_cache)]) -> dict:
    return await _transition(document_id, "Approved", session, cache)


@router.post("/kb/documents/{document_id}/retire")
async def retire_document(document_id: uuid.UUID, session: Annotated[AsyncSession, Depends(get_db)], cache: Annotated[KbCache, Depends(get_kb_cache)]) -> dict:
    return await _transition(document_id, "Retired", session, cache)


@router.post("/kb/search")
async def search(body: SearchRequest, session: Annotated[AsyncSession, Depends(get_db)], cache: Annotated[KbCache, Depends(get_kb_cache)]) -> dict:
    try:
        evidence = await retrieve(session, body.query, body.k, datetime.now(UTC), cache=cache)
    except EmbeddingError as error:
        raise _fail(error) from error
    return {"evidence": [e.model_dump(mode="json") for e in evidence]}
