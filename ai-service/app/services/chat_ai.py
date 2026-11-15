"""Chat và giọng nói: nhờ AI phân loại SẢN PHẨM trong câu, như luồng hóa đơn (AI quyết trước, luật dự phòng).

Luật từ khóa trả lời được khoảng 4/5 câu nhưng không biết sản phẩm lạ ("ổ cắm điện", "bỉm", "photo"), và đôi khi tự tin sai ("cây lau nhà" ra Ăn uống) nên chặn luôn
đường AI. Ở đây AI quyết trước khi người dùng đã đồng ý AI; kết quả khác luật thì không bao giờ tự lưu (người dùng xác nhận); trùng luật thì giữ "ghi nhanh".

An toàn:
- Chỉ gửi CỤM SẢN PHẨM (mô tả đã bỏ số tiền), gộp mọi khoản của một câu vào MỘT lời gọi.
- Thuốc và đồ sức khỏe không bao giờ gửi ra ngoài (luật quyết).
- Thu nhập, chuyển tiền và chi cho người khác ("trả nợ cho Nam") không phải sản phẩm nên không gửi.
- AI lỗi hay chậm quá ``settings.chat_ai_timeout_seconds`` thì giữ kết quả của luật: chat không bị giữ lại vì một bên thứ ba.
Không log nội dung câu hay kết quả.
"""

from __future__ import annotations

import asyncio
import json
import re
from collections.abc import Awaitable, Callable
from dataclasses import replace

import structlog

from app.bill.gemini import LlmClient
from app.bill.item_classifier import classify_items
from app.bill.knowledge import BillKnowledge
from app.core.category_map import INCOME_SLUG, SLUG_BY_LOCAL_CATEGORY
from app.core.config import settings
from app.core.enums import CATEGORY_SLUGS
from app.schemas.bill import ClassifyItemIn
from app.services import intent_cues
from app.services.amount_normalizer import normalize
from app.services.category_resolver import resolve
from app.services.transaction_parser import ChatParse, Entry, has_money_signal, is_question

logger = structlog.get_logger(__name__)

_DIGITS = re.compile(r"\d[\d.,/:]*")
_SLOT = re.compile(r"\{(\d+)\}")
_RECORD_INTENTS = ("add_expense", "add_income", "transfer_money")
# Câu kết thúc bằng "ko", "k", "hong"... (không dấu): người dùng đang HỎI. Bộ luật chỉ biết "không" đầy đủ; "45k" không khớp vì "k" dính liền số.
_ASKS_BY_ABBREVIATION = re.compile(r"(?:^|\s)(?:ko|k|hong|khg|kh)\s*\??$")
_REWRITE_SCHEMA = {"type": "OBJECT", "properties": {"text": {"type": "STRING"}}, "required": ["text"]}
_MAX_REWRITE_CHARS = 400



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
    gemini: LlmClient,
    instruction: str,
    allow_ai: bool,
    *,
    timeout: float | None = None,  # None: lấy từ settings (chat là hội thoại: quá ngưỡng thì dùng luật, không bắt người dùng chờ)
    stats: dict[str, int] | None = None,
) -> ChatParse:
    if not allow_ai:
        return parsed
    targets = [i for i, e in enumerate(parsed.entries) if _is_product(e)]
    if not targets:
        return parsed
    items = [ClassifyItemIn(name=parsed.entries[i].description, parent=None) for i in targets]
    try:
        out, _ = await asyncio.wait_for(classify_items(items, None, knowledge, gemini, instruction, True, stats=stats), settings.chat_ai_timeout_seconds if timeout is None else timeout)
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


def _mask(text: str) -> tuple[str, list[str]]:
    """Thay mọi cụm chữ số (tiền, ngày, giờ) bằng {1}, {2}...: AI chỉ thấy chữ, không bao giờ thấy số tiền; số thật được trả lại sau khi sửa."""
    slots: list[str] = []

    def take(found: re.Match) -> str:
        slots.append(found.group(0))
        return "{%d}" % len(slots)

    return _DIGITS.sub(take, text), slots


def _unmask(rewritten: str, slots: list[str]) -> str | None:
    """Trả số thật vào các ký hiệu; None nếu AI làm sai quy ước (mất, thừa, đổi thứ tự ký hiệu, hoặc tự thêm chữ số): không tin kết quả đó."""
    if [int(n) for n in _SLOT.findall(rewritten)] != list(range(1, len(slots) + 1)):
        return None
    if re.search(r"\d", _SLOT.sub("", rewritten)):
        return None
    return _SLOT.sub(lambda found: slots[int(found.group(1)) - 1], rewritten)


def needs_rewrite(text: str, parsed: ChatParse) -> bool:
    """Luật có vẻ đọc sai: câu nói chuyện tiền mà luật cho là ngoài phạm vi, hoặc câu HỎI mà luật coi là một khoản ghi ("mik mua dt 15tr dc ko")."""
    if parsed.intent == "out_of_scope":
        return has_money_signal(text)
    norm = normalize(text)
    return parsed.intent in _RECORD_INTENTS and (is_question(text, norm) or bool(_ASKS_BY_ABBREVIATION.search(norm)))


async def _rewrite(text: str, gemini: LlmClient, timeout: float) -> str | None:
    prompt = intent_cues.current().replies.get("ai_rewrite_prompt")
    if not prompt:
        return None  # bản cấu hình lưu trước khi có tính năng này, chưa được trộn mặc định
    masked, slots = _mask(text)
    raw = await asyncio.wait_for(gemini.generate_text(f"{prompt}\n\nCÂU: {json.dumps(masked, ensure_ascii=False)}", _REWRITE_SCHEMA), timeout)
    data = json.loads(raw)
    fixed = data.get("text") if isinstance(data, dict) else None
    if not isinstance(fixed, str) or not fixed.strip() or len(fixed) > _MAX_REWRITE_CHARS:
        return None
    return _unmask(fixed.strip(), slots)


async def apply_ai_rewrite(
    text: str,
    parsed: ChatParse,
    reparse: Callable[[str], Awaitable[ChatParse]],
    gemini: LlmClient,
    allow_ai: bool,
    *,
    timeout: float | None = None,
    stats: dict[str, int] | None = None,
) -> ChatParse:
    """Luật không hiểu câu gõ sai/viết tắt thì nhờ Gemini SỬA CHÍNH TẢ rồi để chính luật đọc lại ("t co nen mua iphone 15tr dc k").

    An toàn: chỉ khi người dùng đã đồng ý AI; AI chỉ thấy câu đã che mọi chữ số (số tiền không rời máy) và chỉ trả lại câu đã sửa, không trả nhãn, số tiền hay lời khuyên;
    luật vẫn tách tiền, ngày, danh mục; kết quả chỉ được nhận khi khác kết quả cũ và không phải "ngoài phạm vi"; kết quả nhờ AI không bao giờ tự lưu.
    AI lỗi, chậm, hết cầu dao hay trả sai quy ước thì giữ kết quả của luật. Không log nội dung câu.
    """
    if not allow_ai or not needs_rewrite(text, parsed) or not gemini.available:
        return parsed
    try:
        fixed = await _rewrite(text, gemini, settings.chat_rewrite_timeout_seconds if timeout is None else timeout)
    except Exception as error:  # noqa: BLE001 - AI lỗi/chậm/trả không phải JSON: giữ kết quả của luật
        logger.warning("chat_rewrite_failed", error_type=type(error).__name__)
        if stats is not None:
            stats["rewrite_failed"] = 1
        return parsed
    if not fixed or normalize(fixed) == normalize(text):
        return parsed  # AI không thấy gì cần sửa (hoặc trả sai quy ước)
    again = await reparse(fixed)
    if again.intent == "out_of_scope" or again.intent == parsed.intent:
        return parsed
    if stats is not None:
        stats["rewrite"] = 1
    entries = tuple(replace(e, auto_save_ok=False) for e in again.entries)  # đọc từ câu do AI sửa: người dùng xác nhận
    entry = entries[0] if entries else (replace(again.entry, auto_save_ok=False) if again.entry else None)
    return replace(again, entries=entries, entry=entry)
