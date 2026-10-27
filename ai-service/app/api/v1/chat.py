"""``POST /api/v1/chat/parse`` — câu chat → quyết định (khoản thu/chi/chuyển tiền, hoặc câu hỏi tổng).

``/chat/parse`` không lưu gì và không giữ trạng thái: backend dựng bản nháp, hỏi xác nhận, hoàn tác, sửa danh mục và tự tính tổng. Chỉ
``/chat/unclear`` (và ``/chat/unclear/erase``) ghi câu chưa rõ vào bảng chờ duyệt, khi người dùng đã đồng ý chia sẻ. Không ghi
nội dung câu hay số tiền vào log (AGENTS.md §3.2).
"""

from __future__ import annotations

import time
from dataclasses import replace
from datetime import datetime
from typing import Annotated

import structlog
from fastapi import APIRouter, Depends
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

from app.api.deps import verify_internal_api_key
from app.api.v1.bill import get_ai_instruction, get_bill_knowledge, get_chat_gemini_client, get_classify_stats, get_session_factory
from app.bill.classify_stats import ClassifyStatsRecorder
from app.bill.gemini import GeminiClient
from app.bill.item_classifier import item_key
from app.bill.knowledge import BillKnowledge
from app.services.chat_ai import apply_ai_categories
from app.services.nlu import RasaNlu, open_nlu
from app.services.transaction_parser import VN, ChatParse, parse_chat
from app.core.category_map import INCOME_SLUG, LOAN_SLUG, SLUG_BY_LOCAL_CATEGORY
from app.core.config import settings
from app.core.enums import CATEGORY_SLUGS
from app.schemas.chat import (
    ChatEntry,
    ChatGoal,
    ChatNlu,
    ChatParseRequest,
    ChatParseResponse,
    ChatQuery,
    ChatUnclearEraseRequest,
    ChatUnclearEraseResponse,
    ChatUnclearRequest,
    ChatUnclearResponse,
)
from app.services import category_registry, nlu_candidates

logger = structlog.get_logger(__name__)
router = APIRouter(dependencies=[Depends(verify_internal_api_key)])

_nlu: RasaNlu | None = open_nlu(settings.rasa_url)


def set_nlu(nlu: RasaNlu | None) -> None:
    global _nlu
    _nlu = nlu


async def _with_cached_categories(parsed: ChatParse, knowledge: BillKnowledge) -> ChatParse:
    """Khoản chi chưa rõ nhóm: tra bảng nhớ tên món (câu trả lời Gemini đã lưu từ hóa đơn). Chỉ GỢI Ý: có nhóm nhưng không bao giờ tự ghi,
    vì nguồn gốc là một phỏng đoán. Bảng lỗi thì giữ nguyên kết quả, không làm hỏng câu chat."""
    open_keys = {item_key(e.description) for e in parsed.entries if e.kind == "expense" and e.category is None and e.description}
    open_keys.discard("")
    if not open_keys:
        return parsed
    try:
        cached = await knowledge.cached_items(sorted(open_keys))
    except Exception:  # noqa: BLE001 - chat không được chết vì bảng nhớ
        return parsed

    def fill(entry):
        slug = cached.get(item_key(entry.description)) if entry.kind == "expense" and entry.category is None and entry.description else None
        return replace(entry, category=slug, category_source="cache", auto_save_ok=False) if category_registry.is_spend(slug) else entry

    entries = tuple(fill(e) for e in parsed.entries)
    return replace(parsed, entries=entries, entry=entries[0]) if entries else parsed


def _slug(category: str | None) -> str | None:
    """Nhóm của bộ luật (9 nhóm) hoặc slug backend (từ từ điển) -> slug backend hợp lệ, None nếu không có."""
    if category is None:
        return None
    mapped = SLUG_BY_LOCAL_CATEGORY.get(category, category)
    return mapped if mapped in CATEGORY_SLUGS or mapped == LOAN_SLUG else None


def _to_entry(e) -> ChatEntry:
    return ChatEntry(
        kind=e.kind,
        amount_cents=e.amount,
        description=e.description or None,
        merchant=e.merchant,
        # Thu nhập có một slug "income", trừ khi là tiền vay/được trả nợ: cùng danh mục "Cho vay / Mượn" với chiều chi để báo cáo bỏ cả hai.
        category_slug=(LOAN_SLUG if e.category == LOAN_SLUG else INCOME_SLUG) if e.kind == "income" else _slug(e.category),
        category_source=e.category_source if e.kind == "expense" and _slug(e.category) else None,
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
            category_slugs=[s for c in q.categories if (s := _slug(c))] if q.metric == "expense" else [],
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
        total_amount=parsed.total_amount,
        total_mismatch=parsed.total_mismatch,
        skipped=list(parsed.skipped),
        goal=ChatGoal(
            name=parsed.goal.name,
            name_is_default=parsed.goal.name_is_default,
            target_cents=parsed.goal.target,
            deadline=parsed.goal.deadline,
            deadline_text=parsed.goal.deadline_text,
        )
        if parsed.goal
        else None,
    )


@router.post("/chat/parse", response_model=ChatParseResponse)
async def chat_parse(
    body: ChatParseRequest,
    knowledge: Annotated[BillKnowledge, Depends(get_bill_knowledge)],
    gemini: Annotated[GeminiClient, Depends(get_chat_gemini_client)],
    instruction: Annotated[str, Depends(get_ai_instruction)],
    recorder: Annotated[ClassifyStatsRecorder, Depends(get_classify_stats)],
) -> ChatParseResponse:
    started = time.perf_counter()
    await knowledge.refresh_if_stale()  # từ khóa/thương hiệu trong DB: parse_chat tra chúng qua category_resolver, không có bước này thì chat dùng bản trong image
    nlu = await _nlu.parse(body.text) if _nlu else None
    parsed = parse_chat(
        body.text,
        nlu,
        now=datetime.now(VN),
        auto_save_below=settings.chat_auto_save_below,
    )
    ai_stats: dict[str, int] = {}
    parsed = await apply_ai_categories(parsed, knowledge, gemini, instruction, body.allow_ai, stats=ai_stats)  # AI quyết trước (khi đã đồng ý), luật dự phòng
    parsed = await _with_cached_categories(parsed, knowledge)  # phần còn chưa rõ nhóm: gợi ý từ bảng nhớ
    if body.allow_ai and ai_stats:
        try:
            await recorder.record({f"chat_ai_{k}": v for k, v in ai_stats.items()} | {"chat_ai_messages": 1})  # chỉ số đếm
        except Exception as error:  # noqa: BLE001
            logger.warning("classify_stats_failed", error_type=type(error).__name__)
    response = _to_response(parsed)
    response.processing_ms = int((time.perf_counter() - started) * 1000)
    logger.info("chat_parsed", intent=response.intent, source=response.nlu.source, processing_ms=response.processing_ms)
    return response


@router.post("/chat/unclear", response_model=ChatUnclearResponse)
async def store_unclear(
    body: ChatUnclearRequest, factory: Annotated[async_sessionmaker[AsyncSession], Depends(get_session_factory)]
) -> ChatUnclearResponse:
    """Lưu một câu chat chưa rõ để NGƯỜI duyệt gán nhãn (xem ``app.services.nlu_candidates``). Best-effort như ``/bill/samples``: lỗi DB không bao giờ thành 500.
    Câu có số điện thoại, email, số dài, đường dẫn hay "[" "]" bị từ chối (``stored: false`` kèm lý do). Không ghi nội dung câu vào log."""
    try:
        async with factory() as session:
            reason = await nlu_candidates.add_pending(
                session, user_id_hash=body.user_id_hash, text=body.text, predicted_intent=body.predicted_intent,
                predicted_confidence=body.predicted_confidence, unclear_reason=body.unclear_reason,
            )
    except Exception as error:  # noqa: BLE001 - học từ người dùng là phần phụ, không được làm hỏng luồng chat
        logger.warning("nlu_candidate_store_failed", error_type=type(error).__name__)
        return ChatUnclearResponse(stored=False, reason="error")
    logger.info("nlu_candidate", stored=reason is None, reason=reason)
    return ChatUnclearResponse(stored=reason is None, reason=reason)


@router.post("/chat/unclear/erase", response_model=ChatUnclearEraseResponse)
async def erase_unclear(
    body: ChatUnclearEraseRequest, factory: Annotated[async_sessionmaker[AsyncSession], Depends(get_session_factory)]
) -> ChatUnclearEraseResponse:
    """Xóa mọi câu chưa duyệt của một người dùng (rút đồng ý, xóa tài khoản). Lỗi DB để nổi lên (500): người gọi cần biết việc xóa chưa xong."""
    async with factory() as session:
        deleted = await nlu_candidates.delete_by_user_hash(session, body.user_id_hash)
    logger.info("nlu_candidates_erased", deleted=deleted)
    return ChatUnclearEraseResponse(deleted=deleted)
