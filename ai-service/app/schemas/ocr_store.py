"""Thân request của API lưu trữ dành cho ocr-service (``/api/v1/ocr-store``). Phản hồi là dict thuần.

ocr-service từng kết nối thẳng vào DB của AI Service; từ giờ nó chỉ gọi các endpoint này, nên đổi cấu trúc DB chỉ phải sửa ở AI Service.
Mọi mã người dùng là SHA-256 hex 64 ký tự (AI Service không bao giờ biết id thật).
"""

from __future__ import annotations

from datetime import datetime
from typing import Annotated, Any, Literal

from pydantic import BaseModel, Field

Hash = Annotated[str, Field(pattern=r"^[0-9a-f]{64}$")]
Slug = Annotated[str, Field(pattern=r"^[a-z_]{1,32}$")]
LabelSource = Literal["user", "imported", "gold", "synthetic_ocr"]


class BrandIn(BaseModel):
    brand_key: Annotated[str, Field(min_length=1, max_length=200)]
    category_slug: Slug
    display_name: Annotated[str, Field(min_length=1, max_length=200)]
    whole_word: bool = False


class KeywordIn(BaseModel):
    kind: Literal["merchant", "product"]
    category_slug: Slug
    keyword: Annotated[str, Field(min_length=1, max_length=200)]
    category_priority: Annotated[int, Field(ge=0, le=100000)] = 100


class KnowledgeSeed(BaseModel):
    """Chỉ THÊM mục còn thiếu: dòng đã có (kể cả đã tắt hay đã chuyển nhóm) giữ nguyên, không bao giờ ghi đè."""

    brands: list[BrandIn] = Field(default_factory=list, max_length=5000)
    keywords: list[KeywordIn] = Field(default_factory=list, max_length=20000)


class BrandVote(BrandIn):
    """Thương hiệu do phiếu bầu của người dùng đề xuất (nguồn ``vote``); ``is_active`` false = chờ người duyệt."""

    is_active: bool = False


class MerchantMemoryPut(BaseModel):
    user_id_hash: Hash
    merchant_key: Annotated[str, Field(min_length=1, max_length=200)]
    display_name: Annotated[str | None, Field(max_length=200)] = None
    category_slug: Slug  # slug backend của khoản chi; DB từ chối thu nhập và nhóm cũ


class LabeledSampleIn(BaseModel):
    """Một bill đã gắn nhãn (chữ ĐÃ lọc dữ liệu cá nhân). ``content_hash`` là khóa chống trùng do ocr-service tính (SHA-256 của khóa cửa hàng, tổng tiền và các dòng)."""

    user_id_hash: Hash
    content_hash: Hash
    merchant: Annotated[str, Field(max_length=200)] = ""
    lines: list[Annotated[str, Field(max_length=500)]] = Field(max_length=300)
    total_amount: Annotated[int | None, Field(ge=0)] = None
    category_slug: Slug
    label_source: LabelSource = "user"
    ts: datetime | None = None
    is_gold: bool = False


class GoldClaim(BaseModel):
    total: Annotated[int, Field(ge=0)]
    category_slug: Slug
    dry_run: bool = False


class GoldRename(BaseModel):
    old: Annotated[str, Field(min_length=1, max_length=200)]
    new: Annotated[str, Field(min_length=1, max_length=200)]


class UnknownScanIn(BaseModel):
    user_id_hash: Hash
    content_hash: Hash
    merchant_read: Annotated[str | None, Field(max_length=200)] = None
    merchant_key: Annotated[str, Field(max_length=200)] = ""
    predicted_category: Slug | None = None
    predicted_source: Annotated[str | None, Field(max_length=16)] = None
    confidence: Annotated[float | None, Field(ge=0, le=1)] = None
    total_amount: Annotated[int | None, Field(ge=0)] = None
    lines: list[Annotated[str, Field(max_length=500)]] = Field(max_length=300)


class UnknownScanLabel(BaseModel):
    merchant_key: Annotated[str, Field(min_length=1, max_length=200)]


class TrainingStart(BaseModel):
    stage: Annotated[str, Field(min_length=1, max_length=32)] = "bill_category"
    params: dict[str, Any] = Field(default_factory=dict)


class TrainingFail(BaseModel):
    error: Annotated[str, Field(max_length=2000)]


class TrainingFinish(BaseModel):
    """Kết quả một lần huấn luyện. ``trained`` true = qua cổng chất lượng và thành bản ``active`` (bản active cũ chuyển ``retired``); false = ``rejected``."""

    stage: Annotated[str, Field(min_length=1, max_length=32)] = "bill_category"
    version: Annotated[str, Field(min_length=1, max_length=64)]
    artifact_path: Annotated[str, Field(max_length=500)] = ""
    trained: bool
    n_records: Annotated[int, Field(ge=0)]
    cv_accuracy: Annotated[float | None, Field(ge=0, le=1)] = None
    macro_f1: Annotated[float | None, Field(ge=0, le=1)] = None
    classes: dict[str, int] = Field(default_factory=dict)
    reason: Annotated[str | None, Field(max_length=2000)] = None
    per_class: dict[str, tuple[float, float, float, int]] = Field(default_factory=dict)  # nhãn -> (precision, recall, f1, support)
    min_cv_accuracy: float | None = None
    min_records: int | None = None
