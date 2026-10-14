"""``POST /api/v1/bill/classify`` — chữ đã lọc của một hóa đơn → danh mục.

Bước 3 trong luồng Ảnh → OCR → lọc → AI Service phân loại → BE. Không có trạng thái và không ghi DB: kiến thức (thương hiệu,
từ khóa) đang nằm trong code, sẽ chuyển sang DB của AI cùng bộ nhớ cửa hàng theo người dùng. Không ghi nội dung chữ vào log
(AGENTS.md §3.2).
"""

from __future__ import annotations

import time
import uuid
from typing import Annotated

import structlog
from fastapi import APIRouter, Depends
from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

from app.api.deps import verify_internal_api_key
from app.bill.classifier import CATEGORIES, classify_category_detailed
from app.bill.gemini import GeminiClient
from app.bill.item_classifier import classify_items
from app.bill import samples as bill_samples
from app.bill.knowledge import BillKnowledge, DbBillKnowledge
from app.core.category_map import SLUG_BY_LOCAL_CATEGORY
from app.core.config import settings
from app.db.session import AsyncSessionLocal
from app.core.enums import CATEGORY_SLUGS
from app.schemas.bill import (
    BillClassifyItemsRequest,
    BillClassifyItemsResponse,
    BillClassifyRequest,
    BillClassifyResponse,
    BillLearnRequest,
    BillLearnResponse,
    BillSampleLabelRequest,
    BillSampleLabelResponse,
    BillSampleRequest,
    BillSampleResponse,
)

logger = structlog.get_logger(__name__)
router = APIRouter(dependencies=[Depends(verify_internal_api_key)])

_knowledge = DbBillKnowledge()


def get_bill_knowledge() -> BillKnowledge:
    """Kiến thức từ DB (thương hiệu, từ khóa, cửa hàng người dùng đã chốt). Test thay bằng bản giả qua ``dependency_overrides``."""
    return _knowledge


_gemini = GeminiClient(
    settings.gemini_api_key,
    settings.gemini_model,
    settings.gemini_timeout_seconds,
    fallback_models=tuple(m.strip() for m in settings.gemini_fallback_models.split(",") if m.strip()),
    backup_key=settings.openrouter_api_key,
    backup_model=settings.openrouter_model,
)


def get_gemini_client() -> GeminiClient:
    """Một client dùng chung (giữ cache và khóa mạch 30 giây). Test thay bằng bản giả qua ``dependency_overrides``."""
    return _gemini


_INSTRUCTION_SQL = text("SELECT instruction_text FROM bill_ai_instructions WHERE is_active LIMIT 1")
_INSTRUCTION_TTL_SECONDS = 60.0  # cùng nhịp nạp lại với DbBillKnowledge
_instruction: tuple[float, str] | None = None  # (lúc nạp, nội dung)


async def get_ai_instruction() -> str:
    """Phần chỉ dẫn Gemini sửa được trong DB (dòng đang dùng), có cache. Bảng thiếu/rỗng hoặc DB lỗi → "" (bỏ bước Gemini)."""
    global _instruction
    now = time.monotonic()
    if _instruction is not None and now - _instruction[0] < _INSTRUCTION_TTL_SECONDS:
        return _instruction[1]
    _instruction = (now, _instruction[1] if _instruction else "")  # đặt trước khi nạp: DB hỏng không làm mọi request cùng thử lại
    try:
        async with AsyncSessionLocal() as session:
            value = (await session.execute(_INSTRUCTION_SQL)).scalar_one_or_none()
    except Exception as error:  # noqa: BLE001 - thiếu chỉ dẫn chỉ làm mất bước Gemini, không làm hỏng phân loại
        logger.warning("bill_ai_instruction_load_failed", error=type(error).__name__)
        return _instruction[1]
    _instruction = (now, value or "")
    return _instruction[1]


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


def get_session_factory() -> async_sessionmaker[AsyncSession]:
    """Phiên DB cho mẫu huấn luyện. Test thay bằng factory của DB thử qua ``dependency_overrides``."""
    return AsyncSessionLocal


@router.post("/bill/samples", response_model=BillSampleResponse)
async def store_bill_sample(
    body: BillSampleRequest, factory: Annotated[async_sessionmaker[AsyncSession], Depends(get_session_factory)]
) -> BillSampleResponse:
    """Lưu chữ ĐÃ lọc của một lần quét làm mẫu chờ nhãn. Backend chỉ gọi khi người dùng đã đồng ý; lỗi DB không bao giờ thành 500 (best-effort)."""
    try:
        async with factory() as session:
            stored = await bill_samples.add_pending(session, bill_samples.PendingSample(
                scan_id=body.scan_id, user_id_hash=body.user_id_hash, lines=body.lines, merchant_name=body.merchant_name,
                total_amount=body.total_amount, predicted_slug=body.predicted_slug, predicted_source=body.predicted_source))
    except Exception as error:  # noqa: BLE001 - mẫu học là phần phụ, không được làm hỏng luồng quét
        logger.warning("bill_sample_store_failed", error=type(error).__name__)
        stored = False
    logger.info("bill_sample_stored", stored=stored, lines=len(body.lines))  # không ghi chữ hóa đơn, tên cửa hàng hay số tiền
    return BillSampleResponse(stored=stored)


@router.post("/bill/samples/{scan_id}/label", response_model=BillSampleLabelResponse)
async def label_bill_sample(
    scan_id: uuid.UUID, body: BillSampleLabelRequest, factory: Annotated[async_sessionmaker[AsyncSession], Depends(get_session_factory)]
) -> BillSampleLabelResponse:
    """Gắn nhãn cuối (danh mục người dùng đã chọn) cho mẫu của lần quét này; gắn lại thì nhãn sau thắng."""
    try:
        async with factory() as session:
            labeled = await bill_samples.label(session, scan_id, body.user_id_hash, body.final_slug)
    except Exception as error:  # noqa: BLE001
        logger.warning("bill_sample_label_failed", error=type(error).__name__)
        labeled = False
    logger.info("bill_sample_labeled", labeled=labeled)
    return BillSampleLabelResponse(labeled=labeled)


@router.post("/bill/classify-items", response_model=BillClassifyItemsResponse)
async def classify_bill_items(
    body: BillClassifyItemsRequest,
    knowledge: Annotated[BillKnowledge, Depends(get_bill_knowledge)],
    gemini: Annotated[GeminiClient, Depends(get_gemini_client)],
    instruction: Annotated[str, Depends(get_ai_instruction)],
) -> BillClassifyItemsResponse:
    """Phân loại từng món (không có số tiền): thừa kế → luật → nhóm cửa hàng → Gemini (khi ``allow_ai``) → unknown."""
    started = time.perf_counter()
    merchant = body.merchant_name.strip() if body.merchant_name and body.merchant_name.strip() else None
    items, ai_used = await classify_items(body.items, merchant, knowledge, gemini, instruction, body.allow_ai)
    logger.info(  # chỉ đếm: không ghi tên món, cửa hàng, chỉ dẫn hay kết quả của mô hình
        "bill_items_classified",
        items=len(items),
        unknown=sum(1 for i in items if i.source == "unknown"),
        ai_used=ai_used,
        processing_ms=int((time.perf_counter() - started) * 1000),
    )
    return BillClassifyItemsResponse(items=items, ai_used=ai_used)
