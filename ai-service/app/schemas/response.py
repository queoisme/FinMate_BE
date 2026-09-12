"""Response schema — khớp đúng ARCHITECTURE.md §3.3 và ``AIServiceApiContracts.cs``.

``classifier`` và ``pipeline_result`` luôn có mặt (phía C# khai báo không-nullable); mọi
khối còn lại có thể là null khi pipeline dừng sớm.
"""

from __future__ import annotations

import uuid
from datetime import datetime

from pydantic import BaseModel


class ClassifierResult(BaseModel):
    label: str
    confidence: float


class ExtractionResult(BaseModel):
    amount_cents: int | None = None
    transaction_type: str | None = None
    merchant_name: str | None = None
    description: str | None = None
    transacted_at: datetime | None = None
    balance_after_cents: int | None = None
    confidence: float | None = None


class CategorizationResult(BaseModel):
    category_slug: str | None = None
    confidence: float | None = None


class DuplicateResult(BaseModel):
    is_potential_duplicate: bool = False

    # backend_request_id của thông báo trùng trước đó, KHÔNG phải id nội bộ của AI DB —
    # backend chỉ tra ngược được theo id của chính nó.
    duplicate_request_id: uuid.UUID | None = None


class ModelVersions(BaseModel):
    classifier: str | None = None
    extractor: str | None = None
    categorizer: str | None = None


class AnalyzeResponse(BaseModel):
    pipeline_result: str
    classifier: ClassifierResult
    extraction: ExtractionResult | None = None
    categorization: CategorizationResult | None = None
    duplicate: DuplicateResult | None = None
    model_versions: ModelVersions | None = None
    processing_ms: int | None = None


class FeedbackResponse(BaseModel):
    accepted: bool = True
    feedback_id: uuid.UUID


class ModelStatusResult(BaseModel):
    stage: str
    version: str | None = None
    trained_at: datetime | None = None
    accuracy: float | None = None
    macro_f1: float | None = None
    evaluated_on_split: str | None = None


class TrainingJobResult(BaseModel):
    stage: str
    status: str
    sample_count: int | None = None
    started_at: datetime | None = None
    finished_at: datetime | None = None
    error_message: str | None = None


class StatsResponse(BaseModel):
    """Trả về cho ``GET /api/v1/stats``. Chỉ số đếm ở mức hệ thống — không có gì gắn với
    một người dùng cụ thể."""

    models: list[ModelStatusResult]
    raw_sample_count: int
    labeled_sample_count: int
    unlabeled_sample_count: int
    split_counts: dict[str, int]
    last_training_job: TrainingJobResult | None = None
    pending_feedback_count: int
