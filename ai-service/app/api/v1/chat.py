"""``POST /api/v1/chat/parse`` — câu chat → quyết định (khoản thu/chi/chuyển tiền, hoặc câu hỏi tổng).

``/chat/parse`` không lưu gì và không giữ trạng thái: backend dựng bản nháp, hỏi xác nhận, hoàn tác, sửa danh mục và tự tính tổng.
Không ghi nội dung câu hay số tiền vào log (AGENTS.md §3.2).
"""

from __future__ import annotations

import time
from dataclasses import replace
from datetime import datetime
from typing import Annotated

import structlog
from fastapi import APIRouter, Depends

from app.api.deps import verify_internal_api_key
from app.api.v1.bill import get_ai_instruction, get_bill_knowledge, get_chat_gemini_client, get_classify_stats
from app.bill.classify_stats import ClassifyStatsRecorder
from app.bill.gemini import LlmClient
from app.bill.item_classifier import classify_items, item_key
from app.bill.knowledge import BillKnowledge
from app.services.chat_ai import apply_ai_categories, apply_ai_goal, apply_ai_rewrite
from app.services.intent_classifier import IntentClassifier, shared_classifier
from app.services.transaction_parser import VN, ChatParse, parse_chat
from app.services.vi_text import clean_text
from app.core.category_map import INCOME_SLUG, LOAN_SLUG, SLUG_BY_LOCAL_CATEGORY
from app.core.config import settings
from app.core.enums import CATEGORY_SLUGS
from app.schemas.bill import ClassifyItemIn
from app.schemas.chat import (
    ChatEntry,
    ChatGoal,
    ChatItem,
    ChatNlu,
    ChatParseRequest,
    ChatParseResponse,
    ChatPurchase,
    ChatQuery,
)
from app.services import category_registry
from app.services.item_parser import ItemContext

logger = structlog.get_logger(__name__)
router = APIRouter(dependencies=[Depends(verify_internal_api_key)])

_nlu_override: list[IntentClassifier | None] = []  # có phần tử khi test ghi đè (kể cả None = chỉ dùng luật)


def set_nlu(nlu: IntentClassifier | None) -> None:
    _nlu_override[:] = [nlu]


def _active_nlu() -> IntentClassifier | None:
    return _nlu_override[0] if _nlu_override else shared_classifier()


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


# Nguồn quyết định chắc (bảng nhớ riêng: người dùng đã chọn; luật; nhóm mặc định cửa hàng) thì giữ quyền tự ghi; còn Gemini hay bảng nhớ chung chỉ là gợi ý để người dùng xác nhận.
_DECISIVE_USER_SOURCES = frozenset({"item_memory", "rule", "merchant"})


async def _apply_user_categories(
    response: ChatParseResponse, body: ChatParseRequest, knowledge: BillKnowledge, gemini: LlmClient, instruction: str
) -> None:
    """Xếp khoản chi vào danh mục của CHÍNH người dùng (kể cả danh mục họ tự tạo) bằng đúng bộ phân loại món của hóa đơn (``classify_items``, nhánh cá nhân):
    bảng nhớ riêng → luật (chưa cho phép AI) → bảng nhớ chung → Gemini chọn trong danh sách (đã cho phép AI) → luật. Không tìm ra thì giữ nguyên kết quả cũ (slug hệ thống).
    Không bao giờ làm hỏng câu chat: lỗi thì bỏ qua bước này. Không ghi tên món vào log hay bảng nhớ dùng chung (``remember=False``)."""
    targets = [e for e in response.entries if e.kind == "expense" and e.description]
    if not body.categories or not targets:
        return
    try:
        results, _ = await classify_items(
            [ClassifyItemIn(name=e.description[:200]) for e in targets[:60]], None, knowledge, gemini, instruction, body.allow_ai,
            remember=False, user_id_hash=body.user_id_hash, categories=body.categories,
        )
    except Exception as error:  # noqa: BLE001
        logger.warning("chat_user_categories_failed", error_type=type(error).__name__)
        return
    for entry, result in zip(targets, results):
        if result.category_id is None:
            continue
        entry.category_slug, entry.category_id = result.category_slug, result.category_id
        if result.source in _DECISIVE_USER_SOURCES:
            entry.category_source = "rule"
        else:
            entry.category_source, entry.auto_save_ok = "cache", False


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
            detail=q.detail,
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
            ai_assisted=parsed.goal.ai_assisted,
        )
        if parsed.goal
        else None,
        purchase=ChatPurchase(
            item=parsed.purchase.item,
            amount_cents=parsed.purchase.amount_cents,
            purchase_date=parsed.purchase.purchase_date,
            category_slug=_slug(parsed.purchase.category_slug),
        )
        if parsed.purchase
        else None,
        item=ChatItem(
            kind=parsed.item.kind,
            amount_cents=parsed.item.amount,
            name=parsed.item.name,
            category_slug=_slug(parsed.item.category),
            cadence=parsed.item.cadence,
            next_due=parsed.item.next_due,
            direction=parsed.item.direction,
            counterparty=parsed.item.counterparty,
            operation=parsed.item.operation,
        )
        if parsed.item
        else None,
    )


def _item_context(body: ChatParseRequest) -> ItemContext | None:
    ctx = body.item_context
    if ctx is None:
        return None
    return ItemContext(ctx.kind, ctx.amount_cents, ctx.name, ctx.category_slug, ctx.cadence, ctx.direction, ctx.counterparty)


@router.post("/chat/parse", response_model=ChatParseResponse)
async def chat_parse(
    body: ChatParseRequest,
    knowledge: Annotated[BillKnowledge, Depends(get_bill_knowledge)],
    gemini: Annotated[LlmClient, Depends(get_chat_gemini_client)],
    instruction: Annotated[str, Depends(get_ai_instruction)],
    recorder: Annotated[ClassifyStatsRecorder, Depends(get_classify_stats)],
) -> ChatParseResponse:
    started = time.perf_counter()
    await knowledge.refresh_if_stale()  # từ khóa/thương hiệu trong DB: parse_chat tra chúng qua category_resolver, không có bước này thì chat dùng bản trong image
    text = clean_text(body.text)  # bộ phân loại cũng phải đọc câu đã bỏ ký tự vô hình
    classifier = _active_nlu()
    nlu = await classifier.parse(text) if classifier else None
    parsed = parse_chat(
        text,
        nlu,
        now=datetime.now(VN),
        auto_save_below=settings.chat_auto_save_below,
        item_context=_item_context(body),
    )
    ai_stats: dict[str, int] = {}

    async def reparse(fixed: str) -> ChatParse:
        fixed_nlu = await classifier.parse(fixed) if classifier else None
        return parse_chat(fixed, fixed_nlu, now=datetime.now(VN), auto_save_below=settings.chat_auto_save_below)

    parsed = await apply_ai_rewrite(text, parsed, reparse, gemini, body.allow_ai, stats=ai_stats)  # luật không hiểu câu gõ sai: Gemini sửa chính tả rồi luật đọc lại (khi đã đồng ý AI)
    parsed = await apply_ai_goal(text, parsed, reparse, gemini, body.allow_ai, datetime.now(VN), stats=ai_stats)  # câu đặt mục tiêu mà luật không đọc ra hạn: Gemini chỉ cụm thời hạn, ngày do code tính (khi đã đồng ý AI)
    if not body.categories:  # có danh mục riêng thì ``_apply_user_categories`` bên dưới làm thay cả hai bước (kể cả danh mục hệ thống) trong MỘT vòng Gemini: chạy cả hai làm đôi thời gian chờ và hai lần timeout liền kề mở cầu dao 30 giây
        parsed = await apply_ai_categories(parsed, knowledge, gemini, instruction, body.allow_ai, stats=ai_stats)  # AI quyết trước (khi đã đồng ý), luật dự phòng
        parsed = await _with_cached_categories(parsed, knowledge)  # phần còn chưa rõ nhóm: gợi ý từ bảng nhớ
    if body.allow_ai and ai_stats:
        try:
            await recorder.record({f"chat_ai_{k}": v for k, v in ai_stats.items()} | {"chat_ai_messages": 1})  # chỉ số đếm
        except Exception as error:  # noqa: BLE001
            logger.warning("classify_stats_failed", error_type=type(error).__name__)
    response = _to_response(parsed)
    await _apply_user_categories(response, body, knowledge, gemini, instruction)
    response.processing_ms = int((time.perf_counter() - started) * 1000)
    logger.info("chat_parsed", intent=response.intent, source=response.nlu.source, processing_ms=response.processing_ms)
    return response
