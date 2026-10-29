"""Stage 3b: nhờ AI phân loại phần NỘI DUNG của thông báo khi từ điển không khớp (hàm thuần, chưa nối vào pipeline).

An toàn: chỉ gửi MỘT cụm là phần nội dung do nhãn/trường mô tả của định dạng tách ra, đã qua ``anonymize_body`` và bỏ số tiền; không bao giờ gửi cả thông báo,
số tiền, số dư, số tài khoản. Chi sức khỏe, tiền vào, người dùng chưa đồng ý AI: không gửi. Lỗi/quá hạn/"không rõ" thì giữ kết quả của luật.
Không log nội dung, chỉ loại lỗi.
"""

from __future__ import annotations

import asyncio
import re
from dataclasses import dataclass, replace

import structlog

from app.bill.gemini import GeminiClient
from app.bill.item_classifier import _strip_money, classify_items
from app.bill.knowledge import BillKnowledge
from app.core import confidence as table
from app.core.anonymizer import anonymize_body
from app.core.category_map import INCOME_SLUG
from app.core.enums import CATEGORY_SLUGS, FALLBACK_CATEGORY_SLUG, ExtractionMethod, TransactionType
from app.pipeline.stages.categorizer import CategorizationOutcome
from app.pipeline.stages.extractor import ExtractionOutcome
from app.schemas.bill import ClassifyItemIn
from app.services.category_resolver import resolve

logger = structlog.get_logger(__name__)


@dataclass(frozen=True)
class AiDeps:
    gemini: GeminiClient
    knowledge: BillKnowledge
    instruction: str


# Phần còn sót của anonymize_body ("xxxx5678", "xxxx xxxx xxxx 9012"), số gắn đơn vị tiền ("12345678VND", "50d"), rồi mọi dãy số có từ 4 chữ số trở lên
# kể cả khi các nhóm cách nhau bằng dấu cách . , - / (số điện thoại, số tài khoản, ngày, số tiền, số dư). Số 1-3 chữ số ("2 ly trà sữa") được giữ vì mang nghĩa.
# Giới hạn còn lại: TÊN NGƯỜI trong nội dung ("chuyen tien cho nguyen van a") không loại được ở đây; được che bởi sự đồng ý AI của người dùng.
_MASKED_RE = re.compile(r"\bx{2,}(?:[\s.\-]*x+)*[\s.\-]*\d*", re.IGNORECASE)
_MONEY_UNIT_RE = re.compile(r"\d[\d.,\s]*\s?(?:vnd|đ|d)(?!\w)", re.IGNORECASE)
_NUMBER_RUN_RE = re.compile(r"\d+(?:[\s.,/\-]\d+)*")
_MIN_ID_DIGITS = 4
_MAX_PHRASE_CHARS = 300


def _scrub(text: str) -> str:
    text = _MONEY_UNIT_RE.sub(" ", _MASKED_RE.sub(" ", text))
    text = _NUMBER_RUN_RE.sub(lambda m: " " if sum(c.isdigit() for c in m.group()) >= _MIN_ID_DIGITS else m.group(), text)
    return " ".join(text.split())


def _is_unmatched_rule_result(outcome: CategorizationOutcome) -> bool:
    # Từ điển (merchant_categories.json) không có khóa "other" nên "other" từ luật luôn là không khớp; "other" của mô hình học (method MODEL) là quyết định của mô hình: không đè.
    return outcome.category_slug == FALLBACK_CATEGORY_SLUG and outcome.method == ExtractionMethod.RULE


def _phrase_to_send(extraction: ExtractionOutcome) -> str | None:
    if extraction.transaction_type != TransactionType.DEBIT or not extraction.description_is_narrative or not extraction.description:
        return None
    phrase = _scrub(anonymize_body(extraction.description[:_MAX_PHRASE_CHARS]))  # cắt trước khi che: _MONEY_UNIT_RE bậc hai trên dãy số/dấu cách rất dài
    if not any(c.isalpha() for c in _strip_money(phrase)) or resolve(phrase).slug == "health":
        return None  # rỗng sau khi bỏ số tiền, hoặc đồ sức khỏe: không bao giờ gửi ra ngoài
    return phrase


async def categorize_with_ai(
    outcome: CategorizationOutcome, extraction: ExtractionOutcome, allow_ai: bool, deps: AiDeps | None, *, timeout: float,
) -> CategorizationOutcome:
    if not allow_ai or deps is None or not _is_unmatched_rule_result(outcome):
        return outcome
    phrase = _phrase_to_send(extraction)
    if phrase is None:
        return outcome
    try:
        out, _ = await asyncio.wait_for(
            classify_items([ClassifyItemIn(name=phrase, parent=None)], None, deps.knowledge, deps.gemini, deps.instruction, True, remember=False), timeout,  # không ghi nội dung thông báo vào bảng nhớ dùng chung
        )
    except Exception as error:  # noqa: BLE001 - AI lỗi/chậm: giữ kết quả của luật
        logger.warning("notification_ai_failed", error_type=type(error).__name__)
        return outcome
    result = out[0]
    if result.source not in ("ai", "cache") or result.category_slug not in CATEGORY_SLUGS or result.category_slug == INCOME_SLUG:
        return outcome
    return replace(outcome, category_slug=result.category_slug, confidence=table.get("categorizer.ai"))
