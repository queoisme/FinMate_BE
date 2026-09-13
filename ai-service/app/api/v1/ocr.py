"""``POST /api/v1/ocr`` — ảnh hóa đơn → các trường giao dịch.

Route thứ tư của contract Backend↔AI Service, thêm ở Phase 10 (docx phương thức 3).

Ảnh được xử lý trong bộ nhớ rồi bỏ: không ghi ra đĩa, không vào blob store, không vào AI DB.
Chỉ TEXT đọc được mới lưu lại làm dữ liệu huấn luyện, và cũng đã qua ``anonymize_body``.
Ảnh hóa đơn là một loại dữ liệu cá nhân mới — dựng chỗ chứa nó là một quyết định riêng,
không phải hệ quả phụ của việc làm OCR.
"""

from __future__ import annotations

import time
import uuid
from datetime import UTC, datetime
from typing import Annotated

import structlog
from fastapi import APIRouter, Depends, File, Form, HTTPException, UploadFile, status
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.deps import get_db, verify_internal_api_key
from app.core.anonymizer import anonymize_body, content_hash
from app.core.config import settings
from app.core.enums import ExtractionMethod, PipelineResult, SampleSource
from app.db.models.pipeline_request import PipelineRequest
from app.db.models.raw_sample import RawSample
from app.db.repositories import pipeline_repo, sample_repo
from app.pipeline.models.model_registry import registry
from app.pipeline.stages.categorizer import Categorizer
from app.pipeline.stages.receipt_ocr import (
    OcrEngine,
    TesseractOcrEngine,
    guess_transaction_type,
    read_receipt,
)
from app.schemas.response import CategorizationResult, ExtractionResult, OcrResponse

logger = structlog.get_logger(__name__)
router = APIRouter(dependencies=[Depends(verify_internal_api_key)])

# package_name quy ước cho luồng này, song song với "manual_entry" của luồng NLP. Nó không
# khớp pattern provider nào và không cần khớp — hóa đơn giấy không có template cố định.
RECEIPT_PACKAGE = "receipt_ocr"

# Ảnh chụp bằng điện thoại hiếm khi vượt 5MB sau khi nén. Không có chặn ở đây thì một
# request 50MB sẽ ngốn hết bộ nhớ tiến trình trước khi có ai kịp từ chối nó.
MAX_IMAGE_BYTES = 5 * 1024 * 1024

_ALLOWED_CONTENT_TYPES = frozenset(
    {"image/jpeg", "image/jpg", "image/png", "image/webp", "image/heic", "image/heif"}
)

# Engine thay được để test không cần binary tesseract trên máy chạy test.
_engine: OcrEngine = TesseractOcrEngine()


def set_engine(engine: OcrEngine) -> None:
    global _engine
    _engine = engine


@router.post("/ocr", response_model=OcrResponse)
async def ocr(
    session: Annotated[AsyncSession, Depends(get_db)],
    file: Annotated[UploadFile, File()],
    # Trường form, không phải query param: hash vẫn là định danh người dùng, và query param
    # thì nằm trong URL nên đi vào access log của mọi proxy trên đường.
    user_id_hash: Annotated[str, Form(min_length=64, max_length=64)],
) -> OcrResponse:
    if file.content_type not in _ALLOWED_CONTENT_TYPES:
        raise HTTPException(
            status_code=status.HTTP_415_UNSUPPORTED_MEDIA_TYPE,
            detail=f"Chỉ nhận ảnh: {sorted(_ALLOWED_CONTENT_TYPES)}",
        )

    image = await file.read(MAX_IMAGE_BYTES + 1)
    if len(image) > MAX_IMAGE_BYTES:
        raise HTTPException(
            status_code=status.HTTP_413_REQUEST_ENTITY_TOO_LARGE,
            detail=f"Ảnh tối đa {MAX_IMAGE_BYTES // (1024 * 1024)}MB.",
        )
    if not image:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY, detail="Ảnh rỗng."
        )

    started = time.perf_counter()
    received_at = datetime.now(UTC)

    try:
        reading = read_receipt(_engine, image, received_at)
    except Exception as error:
        # Ảnh hỏng, định dạng lạ, hay thiếu binary tesseract. 500 để backend còn biết đường
        # phân biệt "hệ thống hỏng" với "ảnh không đọc được" (cái sau trả 200 + unreadable).
        logger.error("ocr_engine_failed", error=str(error))
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail="Không chạy được OCR.",
        ) from error

    processing_ms = int((time.perf_counter() - started) * 1000)

    if not reading.text.strip():
        # Không đọc ra chữ nào: không có gì đáng lưu làm dữ liệu huấn luyện, và cũng không
        # có kết quả nào để ghi nhật ký ngoài "ảnh mờ".
        logger.info("ocr_unreadable", processing_ms=processing_ms)
        return OcrResponse(ocr_result="unreadable", processing_ms=processing_ms)

    if reading.amount_cents is None:
        return await _persist(
            session, reading, None, None, "no_amount", processing_ms, user_id_hash
        )

    transaction_type = guess_transaction_type()
    # Chỉ đưa TÊN CỬA HÀNG vào categorizer, không đưa toàn văn hóa đơn: các dòng hàng là
    # nhiễu và chúng át tên cửa hàng. Một hóa đơn WinMart có dòng "Banh mi sandwich" sẽ bị
    # xếp vào "food" thay vì "shopping" — mà danh mục của một lần đi siêu thị là do chỗ mua
    # quyết định, không phải do món đắt nhất trong giỏ.
    categorization = Categorizer(registry).categorize(
        reading.merchant_name,
        None if reading.merchant_name else reading.text,
        transaction_type,
    )

    extraction = ExtractionResult(
        amount_cents=reading.amount_cents,
        transaction_type=transaction_type.value,
        merchant_name=reading.merchant_name,
        description=reading.merchant_name,
        transacted_at=(
            reading.transacted_at.astimezone(UTC) if reading.transacted_at else None
        ),
        balance_after_cents=None,
        confidence=reading.confidence,
    )
    category = CategorizationResult(
        category_slug=categorization.category_slug,
        confidence=categorization.confidence,
    )

    return await _persist(
        session, reading, extraction, category, "success", processing_ms, user_id_hash
    )


async def _persist(
    session: AsyncSession,
    reading,
    extraction: ExtractionResult | None,
    category: CategorizationResult | None,
    result: str,
    processing_ms: int,
    user_id_hash: str,
) -> OcrResponse:
    """Ghi nhật ký như mọi lần chạy pipeline khác, để OCR cũng xuất hiện ở /admin/ai-stats."""
    body = anonymize_body(reading.text)
    raw_sample_id = None
    if settings.collect_raw_samples:
        sample = await sample_repo.add_raw_if_absent(
            session,
            RawSample(
                source=SampleSource.RECEIPT.value,
                package_name=RECEIPT_PACKAGE,
                notification_title=None,
                notification_body=body,
                received_at=datetime.now(UTC),
                user_id_hash=user_id_hash,
                content_hash=content_hash(RECEIPT_PACKAGE, None, body),
            ),
        )
        raw_sample_id = sample.id

    row = PipelineRequest(
        backend_request_id=uuid.uuid4(),
        user_id_hash=user_id_hash,
        package_name=RECEIPT_PACKAGE,
        received_at=datetime.now(UTC),
        pipeline_result=(
            PipelineResult.FINANCIAL.value
            if extraction
            else PipelineResult.EXTRACTION_FAILED.value
        ),
        amount_cents=extraction.amount_cents if extraction else None,
        transaction_type=extraction.transaction_type if extraction else None,
        merchant_name=extraction.merchant_name if extraction else None,
        transacted_at=extraction.transacted_at if extraction else None,
        extraction_confidence=extraction.confidence if extraction else None,
        extraction_method=ExtractionMethod.RULE.value if extraction else None,
        matched_pattern_name=reading.total_marker,
        category_slug=category.category_slug if category else None,
        categorization_confidence=category.confidence if category else None,
        raw_sample_id=raw_sample_id,
        processing_ms=processing_ms,
    )
    await pipeline_repo.add(session, row)
    await session.commit()

    # Không log merchant hay số tiền (AGENTS.md §3.2).
    logger.info("ocr_analyzed", ocr_result=result, processing_ms=processing_ms)

    return OcrResponse(
        ocr_result=result,
        extraction=extraction,
        categorization=category,
        processing_ms=processing_ms,
    )
