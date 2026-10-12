"""``POST /api/v1/chat/parse`` — câu chat → quyết định (khoản thu/chi/chuyển tiền, hoặc câu hỏi tổng).

Không lưu gì và không giữ trạng thái: backend dựng bản nháp, hỏi xác nhận, hoàn tác, sửa danh mục và tự tính tổng. Không ghi
nội dung câu hay số tiền vào log (AGENTS.md §3.2).
"""

from __future__ import annotations

import time
from datetime import datetime

import structlog
from fastapi import APIRouter, Depends

from app.api.deps import verify_internal_api_key
from app.chat.nlu import RasaNlu, open_nlu
from app.chat.parser import VN, ChatParse, parse_chat
from app.core.category_map import INCOME_SLUG, SLUG_BY_LOCAL_CATEGORY
from app.core.config import settings
from app.core.enums import CATEGORY_SLUGS
from app.pipeline.stages.categorizer import categorize_by_rules
from app.schemas.chat import ChatEntry, ChatNlu, ChatParseRequest, ChatParseResponse, ChatQuery

logger = structlog.get_logger(__name__)
router = APIRouter(dependencies=[Depends(verify_internal_api_key)])

# Bảng từ vựng chat chỉ có 9 nhóm và không biết tên cửa hàng riêng; khi nó không đoán được thì thử từ điển cửa hàng của AI Service.
# Chỉ nhận khớp chính xác (độ tin cậy của một lần trúng từ điển), không nhận phỏng đoán "other".
_LEXICON_HIT = 0.90

_nlu: RasaNlu | None = open_nlu(settings.rasa_url)


def set_nlu(nlu: RasaNlu | None) -> None:
    global _nlu
    _nlu = nlu


def _lexicon_category(merchant: str | None, description: str) -> str | None:
    outcome = categorize_by_rules(" ".join(part for part in (merchant, description) if part))
    hit = outcome.confidence >= _LEXICON_HIT and outcome.category_slug != INCOME_SLUG  # chi tiêu thì không thể là "income"
    return outcome.category_slug if hit else None


def _slug(category: str | None) -> str | None:
    """Nhóm của bộ luật (9 nhóm) hoặc slug backend (từ từ điển) -> slug backend hợp lệ, None nếu không có."""
    if category is None:
        return None
    mapped = SLUG_BY_LOCAL_CATEGORY.get(category, category)
    return mapped if mapped in CATEGORY_SLUGS else None


def _to_entry(e) -> ChatEntry:
    return ChatEntry(
        kind=e.kind,
        amount_cents=e.amount,
        description=e.description or None,
        merchant=e.merchant,
        category_slug=INCOME_SLUG if e.kind == "income" else _slug(e.category),
        income_type=e.income_type,
        auto_save_ok=e.auto_save_ok and _slug(e.category) is not None,
        to_others=e.to_others,
        occurred_at=e.occurred_at,
    )


def _to_response(parsed: ChatParse) -> ChatParseResponse:
    entries = [_to_entry(e) for e in parsed.entries]
    entry = entries[0] if entries else None
    query = None
    if parsed.query:
        q = parsed.query
        query = ChatQuery(
            metric=q.metric,
            period_key=q.period_key,
            period_label=q.period_label,
            start=q.start,
            end=q.end,
            category_slug=_slug(q.category) if q.metric == "expense" else None,
            income_type=q.income_type,
        )
    return ChatParseResponse(
        intent=parsed.intent,
        reply_text=parsed.reply_text,
        entry=entry,
        entries=entries,
        query=query,
        nlu=ChatNlu(source=parsed.nlu_source, intent=parsed.nlu_intent, confidence=parsed.nlu_confidence),
        unclear_reason=parsed.unclear_reason,
    )


@router.post("/chat/parse", response_model=ChatParseResponse)
async def chat_parse(body: ChatParseRequest) -> ChatParseResponse:
    started = time.perf_counter()
    nlu = await _nlu.parse(body.text) if _nlu else None
    parsed = parse_chat(
        body.text,
        nlu,
        now=datetime.now(VN),
        auto_save_below=settings.chat_auto_save_below,
        fallback_category=_lexicon_category,
    )
    response = _to_response(parsed)
    response.processing_ms = int((time.perf_counter() - started) * 1000)
    logger.info("chat_parsed", intent=response.intent, source=response.nlu.source, processing_ms=response.processing_ms)
    return response
