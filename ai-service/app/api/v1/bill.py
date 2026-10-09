"""``POST /api/v1/bill/classify`` — chữ đã lọc của một hóa đơn → danh mục.

Bước 3 trong luồng Ảnh → OCR → lọc → AI Service phân loại → BE. Không có trạng thái và không ghi DB: kiến thức (thương hiệu,
từ khóa) đang nằm trong code, sẽ chuyển sang DB của AI cùng bộ nhớ cửa hàng theo người dùng. Không ghi nội dung chữ vào log
(AGENTS.md §3.2).
"""

from __future__ import annotations

import time
from typing import Annotated

import structlog
from fastapi import APIRouter, Depends

from app.api.deps import verify_internal_api_key
from app.bill.classifier import CATEGORIES, classify_category_detailed
from app.bill.knowledge import BillKnowledge, DbBillKnowledge
from app.core.category_map import SLUG_BY_LOCAL_CATEGORY
from app.core.enums import CATEGORY_SLUGS
from app.schemas.bill import BillClassifyRequest, BillClassifyResponse, BillLearnRequest, BillLearnResponse

logger = structlog.get_logger(__name__)
router = APIRouter(dependencies=[Depends(verify_internal_api_key)])

_knowledge = DbBillKnowledge()


def get_bill_knowledge() -> BillKnowledge:
    """Kiến thức từ DB (thương hiệu, từ khóa, cửa hàng người dùng đã chốt). Test thay bằng bản giả qua ``dependency_overrides``."""
    return _knowledge


_MAX_LINE_CHARS = 200
# Kết quả của luồng hóa đơn luôn cần người xem lại; trần này khớp trần của OCR (remote_ocr.py).
_MAX_CONFIDENCE = 0.80


@router.post("/bill/classify", response_model=BillClassifyResponse)
async def classify_bill(
    body: BillClassifyRequest, knowledge: Annotated[BillKnowledge, Depends(get_bill_knowledge)]
) -> BillClassifyResponse:
    started = time.perf_counter()
    lines = [line[:_MAX_LINE_CHARS] for line in body.lines]
    merchant = body.merchant_name.strip() if body.merchant_name and body.merchant_name.strip() else None

    await knowledge.refresh_if_stale()
    memory = await knowledge.remembered(body.user_id_hash, merchant)
    if memory and memory.backend_slug and memory.category in CATEGORY_SLUGS:
        # Người dùng đã chọn đúng slug backend khi lưu giao dịch: trả nguyên, không đổi qua 9 nhóm (sẽ mất phân biệt food = Ăn uống / Siêu thị).
        return BillClassifyResponse(
            category_slug=memory.category, local_category=None, confidence=_MAX_CONFIDENCE, source="memory",
            processing_ms=int((time.perf_counter() - started) * 1000))
    remembered = memory.category if memory and not memory.backend_slug and memory.category in CATEGORIES else None  # nhóm lạ trong DB thì bỏ qua
    category, source, confidence = classify_category_detailed(merchant, lines, remembered=remembered)
    known = source != "none"

    response = BillClassifyResponse(
        category_slug=SLUG_BY_LOCAL_CATEGORY.get(category) if known else None,
        local_category=category if known else None,
        confidence=min(confidence, _MAX_CONFIDENCE) if known else 0.0,
        source=source,
        processing_ms=int((time.perf_counter() - started) * 1000),
    )
    logger.info("bill_classified", source=response.source, lines=len(lines), processing_ms=response.processing_ms)
    return response


@router.post("/bill/learn", response_model=BillLearnResponse)
async def learn_bill_merchant(
    body: BillLearnRequest, knowledge: Annotated[BillKnowledge, Depends(get_bill_knowledge)]
) -> BillLearnResponse:
    """Ghi nhớ danh mục người dùng chọn cho cửa hàng (theo hash người dùng); lần quét sau ``/bill/classify`` trả đúng danh mục đó."""
    learned = await knowledge.learn(body.user_id_hash, body.merchant_name, body.category_slug)
    logger.info("bill_merchant_learned", learned=learned)  # không ghi tên cửa hàng hay danh mục
    return BillLearnResponse(learned=learned)
