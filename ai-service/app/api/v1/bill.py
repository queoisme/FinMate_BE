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

from app.bill.classify_stats import ClassifyStatsRecorder, build_counts
from app.bill.gemini import GeminiClient
from app.bill.item_classifier import classify_items
from app.bill import item_votes
from app.bill import samples as bill_samples
from app.bill import slug_model
from app.bill.rules_classify import MAX_CONFIDENCE as _MAX_CONFIDENCE
from app.bill.rules_classify import classify_by_rules
from app.bill.knowledge import BillKnowledge, DbBillKnowledge
from app.core.category_map import INCOME_SLUG
from app.services.category_resolver import resolve
from app.core.config import settings
from app.db.session import AsyncSessionLocal
from app.core.enums import CATEGORY_SLUGS
from app.services import category_registry
from app.schemas.bill import (
    BillClassifyItemsRequest,
    BillItemFeedbackRequest,
    BillItemFeedbackResponse,
    BillClassifyItemsResponse,
    BillClassifyRequest,
    BillClassifyResponse,
    BillLearnRequest,
    BillLearnResponse,
    BillSampleDiscardRequest,
    BillSampleDiscardResponse,
    BillSampleEraseRequest,
    BillSampleEraseResponse,
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


_classify_stats = ClassifyStatsRecorder()


def get_classify_stats() -> ClassifyStatsRecorder:
    """Bộ đếm mức dùng (chỉ số đếm theo ngày). Test thay bằng bản giả qua ``dependency_overrides``."""
    return _classify_stats


# Chat có client riêng: chuỗi model nhanh hơn, trần thời gian chặt hơn, không đi nhà cung cấp dự phòng (OpenRouter), và cầu dao tách biệt với luồng hóa đơn
# (hóa đơn tải nặng không làm chat mất AI, và ngược lại).
_chat_gemini = GeminiClient(
    settings.gemini_api_key,
    settings.chat_gemini_model,
    settings.chat_gemini_timeout_seconds,
    overall_timeout=settings.chat_gemini_overall_seconds,
    fallback_models=tuple(m.strip() for m in settings.chat_gemini_fallback_models.split(",") if m.strip()),
)


def get_chat_gemini_client() -> GeminiClient:
    """Gemini cho chat và giọng nói. Test thay bằng bản giả qua ``dependency_overrides``."""
    return _chat_gemini


def get_gemini_client() -> GeminiClient:
    """Một client dùng chung (giữ cache và khóa mạch 30 giây). Test thay bằng bản giả qua ``dependency_overrides``."""
    return _gemini


_INSTRUCTION_SQL = text("SELECT instruction_text FROM kb_ai_instructions WHERE is_active LIMIT 1")
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


def invalidate_ai_instruction() -> None:
    """Chỉ dẫn vừa đổi qua API quản trị: lần gọi Gemini kế tiếp đọc lại từ DB thay vì chờ hết nhịp 60 giây."""
    global _instruction
    _instruction = None


_MAX_LINE_CHARS = 200


@router.post("/bill/classify", response_model=BillClassifyResponse)
async def classify_bill(
    body: BillClassifyRequest, knowledge: Annotated[BillKnowledge, Depends(get_bill_knowledge)]
) -> BillClassifyResponse:
    started = time.perf_counter()
    lines = [line[:_MAX_LINE_CHARS] for line in body.lines]
    merchant = body.merchant_name.strip() if body.merchant_name and body.merchant_name.strip() else None

    await knowledge.refresh_if_stale()
    memory = await knowledge.remembered(body.user_id_hash, merchant)
    if memory and category_registry.is_known(memory):
        # Người dùng đã chọn đúng slug backend khi lưu giao dịch: trả nguyên, không đổi qua 9 nhóm (sẽ mất phân biệt food = Ăn uống / Siêu thị).
        return BillClassifyResponse(
            category_slug=memory, local_category=None, confidence=_MAX_CONFIDENCE, source="memory",
            processing_ms=int((time.perf_counter() - started) * 1000))
    result = classify_by_rules(merchant, lines)
    if result.source == "none" and (model := slug_model.active()):  # luật không biết: mô hình đã duyệt chỉ trả lời khi đủ tự tin
        guess, probability = model.predict(merchant, lines)
        if probability >= slug_model.MIN_PROBABILITY and guess in CATEGORY_SLUGS and guess != INCOME_SLUG:
            return BillClassifyResponse(
                category_slug=guess, local_category=None, confidence=min(probability, _MAX_CONFIDENCE), source="model",
                processing_ms=int((time.perf_counter() - started) * 1000))

    response = BillClassifyResponse(
        category_slug=result.category_slug,
        local_category=result.local_category,
        confidence=result.confidence,
        source=result.source,
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
                predicted_slug=body.predicted_slug, predicted_source=body.predicted_source))
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


@router.post("/bill/samples/{scan_id}/discard", response_model=BillSampleDiscardResponse)
async def discard_bill_sample(
    scan_id: uuid.UUID, body: BillSampleDiscardRequest, factory: Annotated[async_sessionmaker[AsyncSession], Depends(get_session_factory)]
) -> BillSampleDiscardResponse:
    """Xóa mẫu của lần quét này (chỉ của đúng người dùng). Best-effort như ``label``: lỗi chỉ ghi log."""
    try:
        async with factory() as session:
            discarded = await bill_samples.discard(session, scan_id, body.user_id_hash)
    except Exception as error:  # noqa: BLE001
        logger.warning("bill_sample_discard_failed", error=type(error).__name__)
        discarded = False
    logger.info("bill_sample_discarded", discarded=discarded)
    return BillSampleDiscardResponse(discarded=discarded)


@router.post("/bill/samples/erase", response_model=BillSampleEraseResponse)
async def erase_bill_samples(
    body: BillSampleEraseRequest, factory: Annotated[async_sessionmaker[AsyncSession], Depends(get_session_factory)]
) -> BillSampleEraseResponse:
    """Xóa mọi mẫu huấn luyện VÀ phiếu món của người dùng này (cả mẫu đã có nhãn). Lỗi DB để lan ra (500): người gọi cần biết việc xóa chưa xong."""
    async with factory() as session:
        deleted = await bill_samples.delete_by_user_hash(session, body.user_id_hash)
        votes = await item_votes.delete_by_user_hash(session, body.user_id_hash)
    logger.info("bill_samples_erased", deleted=deleted, item_votes=votes)
    return BillSampleEraseResponse(deleted=deleted, item_votes=votes)


@router.post("/bill/items/feedback", response_model=BillItemFeedbackResponse)
async def record_item_feedback(
    body: BillItemFeedbackRequest, factory: Annotated[async_sessionmaker[AsyncSession], Depends(get_session_factory)]
) -> BillItemFeedbackResponse:
    """Ghi phiếu danh mục cho từng món (mỗi người một phiếu cho mỗi món, phiếu sau thay phiếu trước). Backend chỉ gọi khi người dùng đã đồng ý đóng góp dữ liệu.
    Best-effort như ``/bill/samples``: lỗi DB không bao giờ thành 500, vì phiếu là phần phụ của việc lưu hóa đơn."""
    try:
        async with factory() as session:
            recorded = await item_votes.record_votes(session, body.user_id_hash, [(item.name, item.category_slug) for item in body.items])
    except Exception as error:  # noqa: BLE001 - phiếu học là phần phụ, không được làm hỏng luồng lưu hóa đơn
        logger.warning("bill_item_feedback_failed", error=type(error).__name__)
        recorded = 0
    logger.info("bill_item_feedback", recorded=recorded, items=len(body.items))  # không ghi tên món
    return BillItemFeedbackResponse(recorded=recorded)


@router.post("/bill/classify-items", response_model=BillClassifyItemsResponse)
async def classify_bill_items(
    body: BillClassifyItemsRequest,
    knowledge: Annotated[BillKnowledge, Depends(get_bill_knowledge)],
    gemini: Annotated[GeminiClient, Depends(get_gemini_client)],
    instruction: Annotated[str, Depends(get_ai_instruction)],
    recorder: Annotated[ClassifyStatsRecorder, Depends(get_classify_stats)],
) -> BillClassifyItemsResponse:
    """Phân loại từng món (không có số tiền): thừa kế → luật → nhóm cửa hàng → Gemini (khi ``allow_ai``) → unknown."""
    started = time.perf_counter()
    merchant = body.merchant_name.strip() if body.merchant_name and body.merchant_name.strip() else None
    stats: dict[str, int] = {}
    items, ai_used = await classify_items(body.items, merchant, knowledge, gemini, instruction, body.allow_ai, stats=stats)
    try:
        await recorder.record(build_counts(items, body.allow_ai, ai_used, stats))  # đo mức dùng: chỉ số đếm, lỗi thì bỏ qua
    except Exception as error:  # noqa: BLE001
        logger.warning("classify_stats_failed", error_type=type(error).__name__)
    logger.info(  # chỉ đếm: không ghi tên món, cửa hàng, chỉ dẫn hay kết quả của mô hình
        "bill_items_classified",
        items=len(items),
        unknown=sum(1 for i in items if i.source == "unknown"),
        ai_used=ai_used,
        processing_ms=int((time.perf_counter() - started) * 1000),
    )
    return BillClassifyItemsResponse(items=items, ai_used=ai_used)
