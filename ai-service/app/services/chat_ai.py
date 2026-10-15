"""Chat và giọng nói: nhờ AI phân loại SẢN PHẨM trong câu, như luồng hóa đơn (AI quyết trước, luật dự phòng).

Luật từ khóa trả lời được khoảng 4/5 câu nhưng không biết sản phẩm lạ ("ổ cắm điện", "bỉm", "photo"), và đôi khi tự tin sai ("cây lau nhà" ra Ăn uống) nên chặn luôn
đường AI. Ở đây AI quyết trước khi người dùng đã đồng ý AI; kết quả khác luật thì không bao giờ tự lưu (người dùng xác nhận); trùng luật thì giữ "ghi nhanh".

An toàn:
- Chỉ gửi CỤM SẢN PHẨM (mô tả đã bỏ số tiền), gộp mọi khoản của một câu vào MỘT lời gọi.
- Thuốc và đồ sức khỏe không bao giờ gửi ra ngoài (luật quyết).
- Thu nhập, chuyển tiền và chi cho người khác ("trả nợ cho Nam") không phải sản phẩm nên không gửi.
- AI lỗi hay chậm quá ``CHAT_AI_TIMEOUT_SECONDS`` thì giữ kết quả của luật: chat không bị giữ lại vì một bên thứ ba.
Không log nội dung câu hay kết quả.
"""

from __future__ import annotations

import asyncio
from dataclasses import replace

import structlog

from app.bill.gemini import GeminiClient
from app.bill.item_classifier import classify_items
from app.bill.knowledge import BillKnowledge
from app.core.category_map import INCOME_SLUG, SLUG_BY_LOCAL_CATEGORY
from app.core.enums import CATEGORY_SLUGS
from app.schemas.bill import ClassifyItemIn
from app.services.category_resolver import resolve
from app.services.transaction_parser import ChatParse, Entry

logger = structlog.get_logger(__name__)

CHAT_AI_TIMEOUT_SECONDS = 6.0  # chat là hội thoại: quá ngưỡng này thì dùng luật, không bắt người dùng chờ


def _backend_slug(category: str | None) -> str | None:
    if category is None:
        return None
    mapped = SLUG_BY_LOCAL_CATEGORY.get(category, category)
    return mapped if mapped in CATEGORY_SLUGS else None


def _is_product(entry: Entry) -> bool:
    if entry.kind != "expense" or entry.to_others or not entry.description.strip():
        return False
    return _backend_slug(entry.category) != "health" and resolve(entry.description).slug != "health"  # thuốc: không bao giờ gửi


async def apply_ai_categories(
    parsed: ChatParse,
    knowledge: BillKnowledge,
    gemini: GeminiClient,
    instruction: str,
    allow_ai: bool,
    *,
    timeout: float = CHAT_AI_TIMEOUT_SECONDS,
    stats: dict[str, int] | None = None,
) -> ChatParse:
    if not allow_ai:
        return parsed
    targets = [i for i, e in enumerate(parsed.entries) if _is_product(e)]
    if not targets:
        return parsed
    items = [ClassifyItemIn(name=parsed.entries[i].description, parent=None) for i in targets]
    try:
        out, _ = await asyncio.wait_for(classify_items(items, None, knowledge, gemini, instruction, True, stats=stats), timeout)
    except Exception as error:  # noqa: BLE001 - AI lỗi/chậm: giữ kết quả của luật
        logger.warning("chat_ai_failed", error_type=type(error).__name__)
        if stats is not None:
            stats["ai_failed"] = 1
        return parsed

    entries = list(parsed.entries)
    for i, result in zip(targets, out):
        slug = result.category_slug
        if result.source not in ("ai", "cache") or slug not in CATEGORY_SLUGS or slug == INCOME_SLUG:
            continue  # AI không quyết được ("không rõ"): giữ luật
        entry = entries[i]
        if _backend_slug(entry.category) == slug:
            continue  # luật và AI cùng ý: giữ nguyên, kể cả "ghi nhanh"
        entries[i] = replace(entry, category=slug, category_source=result.source, auto_save_ok=False)  # AI quyết khác luật: người dùng xác nhận
        if stats is not None:
            stats["changed"] = stats.get("changed", 0) + 1
    return replace(parsed, entries=tuple(entries), entry=entries[0])
