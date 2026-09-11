"""Điều phối 4 stage và ghi nhật ký chạy.

Thứ tự và điều kiện dừng sớm theo đúng ARCHITECTURE.md §3.2:

    Classifier → (chỉ khi financial) Extractor → Categorizer → Duplicate Detector → log

Mọi lần chạy đều ghi một dòng ``pipeline_requests``, kể cả khi dừng sớm hay lỗi — không có
dòng đó thì Duplicate Detector mù và không ai gỡ được lỗi phân loại ngoài production.
"""

from __future__ import annotations

import time
import uuid
from datetime import UTC, datetime

import structlog
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.anonymizer import anonymize_body, content_hash
from app.core.config import settings
from app.core.enums import (
    ClassifierLabel,
    ModelStage,
    PipelineResult,
    SampleSource,
)
from app.db.models.pipeline_request import PipelineRequest
from app.db.models.raw_sample import RawSample
from app.db.repositories import pattern_repo, pipeline_repo, sample_repo
from app.pipeline.models.model_registry import ModelRegistry, registry
from app.pipeline.stages import duplicate_detector
from app.pipeline.stages.categorizer import Categorizer
from app.pipeline.stages.classifier import MANUAL_ENTRY_PACKAGE, Classifier
from app.pipeline.stages.extractor import Extractor
from app.schemas.request import AnalyzeRequest
from app.schemas.response import (
    AnalyzeResponse,
    CategorizationResult,
    ClassifierResult,
    DuplicateResult,
    ExtractionResult,
    ModelVersions,
)
from app.utils.provider_patterns import PatternMatcher, PatternSpec

logger = structlog.get_logger(__name__)

# Extractor chạy bằng luật nên "version" của nó là version của BỘ LUẬT, không phải của một
# artifact. Bump tay khi thay đổi ngữ nghĩa bóc trường, để một dòng pipeline_requests cũ
# vẫn giải thích được bằng đúng bộ luật đã sinh ra nó.
EXTRACTOR_RULE_VERSION = "rule-1.0.0"

PATTERN_RELOAD_INTERVAL_SECONDS = 60.0


class Pipeline:
    def __init__(self, model_registry: ModelRegistry) -> None:
        self._registry = model_registry
        self._matcher = PatternMatcher([])
        self._patterns_checked_at = 0.0

    async def refresh(self, session: AsyncSession, force: bool = False) -> None:
        """Nạp lại pattern và model đang active.

        Cả hai đều sửa được bằng UPDATE dữ liệu mà không cần deploy; đổi lại phải có chỗ
        nào đó nhận ra thay đổi. 60 giây là đủ nhanh cho người vận hành và đủ rẻ cho
        đường request.
        """
        now = time.monotonic()
        if force or now - self._patterns_checked_at >= PATTERN_RELOAD_INTERVAL_SECONDS:
            self._patterns_checked_at = now
            rows = await pattern_repo.list_active(session)
            self._matcher = PatternMatcher(
                [
                    PatternSpec(
                        provider_key=row.provider_key,
                        package_name=row.package_name,
                        pattern_name=row.pattern_name,
                        regex=row.regex,
                        transaction_type=row.transaction_type,
                        priority=row.priority,
                        sample_text=row.sample_text,
                    )
                    for row in rows
                ]
            )
        await self._registry.ensure_loaded(session, force=force)

    async def analyze(
        self, session: AsyncSession, request: AnalyzeRequest
    ) -> AnalyzeResponse:
        started = time.perf_counter()
        await self.refresh(session)

        existing = await pipeline_repo.get_by_backend_request_id(
            session, request.backend_request_id
        )
        if existing is not None:
            # Backend retry (RetryFailedNotificationJob) gửi lại đúng request cũ. Chạy lại
            # pipeline sẽ tự dò trùng với chính dòng cũ của mình — trả lại kết quả đã lưu.
            duplicate_backend_id = None
            if existing.duplicate_of_request_id is not None:
                duplicate_backend_id = await pipeline_repo.get_backend_request_id(
                    session, existing.duplicate_of_request_id
                )
            return _response_from_row(existing, duplicate_backend_id)

        text = " ".join(
            part
            for part in (request.notification_title, request.notification_body)
            if part
        )

        classification = Classifier(self._registry).classify(text, request.package_name)
        extraction = None
        categorization = None
        duplicate = duplicate_detector.DuplicateOutcome(False)
        result = PipelineResult.NON_FINANCIAL

        if classification.label == ClassifierLabel.FINANCIAL:
            extraction = Extractor(self._matcher).extract(
                request.package_name,
                request.notification_body,
                request.received_at,
                request.notification_title,
            )
            if extraction is None:
                result = PipelineResult.EXTRACTION_FAILED
            else:
                result = PipelineResult.FINANCIAL
                categorization = Categorizer(self._registry).categorize(
                    extraction.merchant_name,
                    extraction.description,
                    extraction.transaction_type,
                )
                duplicate = await duplicate_detector.detect(
                    session,
                    user_id_hash=request.user_id_hash,
                    amount_cents=extraction.amount_cents,
                    transacted_at=extraction.transacted_at,
                    backend_request_id=request.backend_request_id,
                )
        elif classification.label == ClassifierLabel.UNCERTAIN:
            result = PipelineResult.UNCERTAIN

        processing_ms = int((time.perf_counter() - started) * 1000)

        # Thu thập mẫu TRƯỚC khi ghi pipeline_requests: dòng nhật ký cần mang theo id của
        # mẫu, nếu không phản hồi của người dùng về sau không lần ngược được về câu gốc.
        raw_sample_id = None
        if settings.collect_raw_samples:
            raw_sample_id = await self._collect_sample(session, request)

        row = await self._persist(
            session,
            request,
            classification,
            extraction,
            categorization,
            duplicate,
            result,
            processing_ms,
            raw_sample_id,
        )

        # Không log notification_body, amount_cents hay merchant (AGENTS.md §3.2) — chỉ
        # những trường đủ để theo dõi vận hành.
        logger.info(
            "pipeline_analyzed",
            backend_request_id=str(request.backend_request_id),
            package_name=request.package_name,
            pipeline_result=result.value,
            classifier_confidence=round(classification.confidence, 3),
            is_duplicate=duplicate.is_potential_duplicate,
            processing_ms=processing_ms,
        )
        return _response_from_row(row, duplicate.duplicate_request_id)

    async def _persist(
        self,
        session,
        request,
        classification,
        extraction,
        categorization,
        duplicate,
        result,
        processing_ms,
        raw_sample_id,
    ) -> PipelineRequest:
        row = PipelineRequest(
            backend_request_id=request.backend_request_id,
            user_id_hash=request.user_id_hash,
            package_name=request.package_name,
            received_at=request.received_at,
            pipeline_result=result.value,
            classifier_label=classification.label.value,
            classifier_confidence=classification.confidence,
            is_potential_duplicate=duplicate.is_potential_duplicate,
            duplicate_of_request_id=duplicate.duplicate_of_row_id,
            classifier_version=self._registry.version(ModelStage.CLASSIFIER),
            categorizer_version=self._registry.version(ModelStage.CATEGORIZER),
            raw_sample_id=raw_sample_id,
            processing_ms=processing_ms,
        )
        if extraction is not None:
            row.amount_cents = extraction.amount_cents
            row.transaction_type = extraction.transaction_type.value
            row.merchant_name = extraction.merchant_name
            row.description = extraction.description
            row.transacted_at = extraction.transacted_at
            row.balance_after_cents = extraction.balance_after_cents
            row.extraction_confidence = extraction.confidence
            row.extraction_method = extraction.method.value
            row.matched_pattern_name = extraction.matched_pattern_name
            row.extractor_version = EXTRACTOR_RULE_VERSION
        if categorization is not None:
            row.category_slug = categorization.category_slug
            row.categorization_confidence = categorization.confidence
        return await pipeline_repo.add(session, row)

    async def _collect_sample(
        self, session: AsyncSession, request: AnalyzeRequest
    ) -> uuid.UUID:
        body = anonymize_body(request.notification_body)
        source = (
            SampleSource.MANUAL_ENTRY
            if request.package_name == MANUAL_ENTRY_PACKAGE
            else SampleSource.NOTIFICATION
        )
        sample = await sample_repo.add_raw_if_absent(
            session,
            RawSample(
                source=source.value,
                package_name=request.package_name,
                notification_title=request.notification_title,
                notification_body=body,
                received_at=request.received_at,
                user_id_hash=request.user_id_hash,
                content_hash=content_hash(
                    request.package_name, request.notification_title, body
                ),
            ),
        )
        return sample.id


def _to_utc(moment: datetime | None) -> datetime | None:
    """Luôn phát ra mốc thời gian ở offset 0.

    Hai lý do, lý do thứ hai là lý do cứng:

    1. Lần chạy đầu trả giá trị còn nguyên offset của thông báo, lần replay trả giá trị
       đọc từ TIMESTAMPTZ (tức UTC) — cùng một thời điểm nhưng hai chuỗi khác nhau.
    2. **Npgsql từ chối ghi ``DateTimeOffset`` có offset khác 0 vào cột ``timestamptz``.**
       Backend đưa thẳng ``extraction.transacted_at`` vào ``transactions.transacted_at``,
       nên trả "+07:00" làm mọi thông báo tài chính đổ 500 ở phía backend. Cùng cái bẫy đã
       ghi ở TASKS.md Phase 5 (biên chu kỳ ngân sách) — lần này ở đầu bên kia contract.

    Giờ Việt Nam vẫn là thứ tính ra mốc này (xem ``text_utils.parse_datetime``); chỉ có
    biểu diễn khi truyền đi là UTC.
    """
    return moment.astimezone(UTC) if moment is not None else None


def _response_from_row(
    row: PipelineRequest, duplicate_backend_request_id: uuid.UUID | None = None
) -> AnalyzeResponse:
    """Dựng response TỪ dòng đã lưu, không từ biến trong bộ nhớ.

    Như vậy thứ backend nhận được và thứ nằm trong AI DB không thể lệch nhau — mọi ràng
    buộc CHECK đều đã chạy qua trước khi response ra khỏi service.
    """
    extraction = None
    if row.amount_cents is not None:
        extraction = ExtractionResult(
            amount_cents=row.amount_cents,
            transaction_type=row.transaction_type,
            merchant_name=row.merchant_name,
            description=row.description,
            transacted_at=_to_utc(row.transacted_at),
            balance_after_cents=row.balance_after_cents,
            confidence=row.extraction_confidence,
        )

    categorization = None
    if row.category_slug is not None:
        categorization = CategorizationResult(
            category_slug=row.category_slug,
            confidence=row.categorization_confidence,
        )

    return AnalyzeResponse(
        pipeline_result=row.pipeline_result,
        classifier=ClassifierResult(
            label=row.classifier_label or ClassifierLabel.UNCERTAIN.value,
            confidence=row.classifier_confidence or 0.0,
        ),
        extraction=extraction,
        categorization=categorization,
        duplicate=DuplicateResult(
            is_potential_duplicate=row.is_potential_duplicate,
            duplicate_request_id=duplicate_backend_request_id,
        ),
        model_versions=ModelVersions(
            classifier=row.classifier_version,
            extractor=row.extractor_version,
            categorizer=row.categorizer_version,
        ),
        processing_ms=row.processing_ms,
    )


pipeline = Pipeline(registry)
