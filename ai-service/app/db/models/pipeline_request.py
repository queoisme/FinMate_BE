"""``pipeline_requests`` — nhật ký mọi lần ``POST /api/v1/analyze`` chạy.

Vừa là log vận hành, vừa là nguồn dữ liệu DUY NHẤT của Duplicate Detector: bước 4 trong
ARCHITECTURE.md §3.2 truy vấn chính bảng này trong cửa sổ 5 phút.

> Bảng này LƯU ``amount_cents``. Bắt buộc — không có cột đó thì không dò trùng được.
> AGENTS.md §3.2 cấm **log** ``amount_cents``, không cấm lưu; xem ``app/core/logging.py``
> cho phần cấm log.
"""

from sqlalchemy import (
    BigInteger,
    Boolean,
    CheckConstraint,
    Float,
    ForeignKey,
    Index,
    Integer,
    String,
    Text,
    UniqueConstraint,
    text,
)
from sqlalchemy.dialects.postgresql import UUID
from sqlalchemy.orm import Mapped, mapped_column

from app.core.enums import (
    CATEGORY_SLUGS,
    ClassifierLabel,
    ExtractionMethod,
    PipelineResult,
    TransactionType,
)
from app.db.base import Base, created_at, enum_check, short_text, timestamptz, uuid_pk

_CATEGORY_LIST = ", ".join(f"'{slug}'" for slug in CATEGORY_SLUGS)


class PipelineRequest(Base):
    __tablename__ = "pipeline_requests"

    id: Mapped[uuid_pk]

    # Id của notification_log bên backend. Tên "backend_request_id" là chữ của contract
    # (ARCHITECTURE.md §3.3), giữ nguyên để tra ngược được giữa hai hệ thống.
    backend_request_id: Mapped[UUID] = mapped_column(UUID(as_uuid=True), nullable=False)

    user_id_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    package_name: Mapped[short_text]
    received_at: Mapped[timestamptz]

    pipeline_result: Mapped[str] = mapped_column(String(32), nullable=False)

    classifier_label: Mapped[str | None] = mapped_column(String(32))
    classifier_confidence: Mapped[float | None] = mapped_column(Float)

    amount_cents: Mapped[int | None] = mapped_column(BigInteger)
    transaction_type: Mapped[str | None] = mapped_column(String(16))
    merchant_name: Mapped[str | None] = mapped_column(Text)
    description: Mapped[str | None] = mapped_column(Text)
    transacted_at: Mapped[timestamptz | None] = mapped_column()
    balance_after_cents: Mapped[int | None] = mapped_column(BigInteger)
    extraction_confidence: Mapped[float | None] = mapped_column(Float)
    extraction_method: Mapped[str | None] = mapped_column(String(16))

    # Pattern nào đã khớp — không có cột này thì gỡ lỗi "sao giao dịch ra sai số tiền"
    # phải đoán mò trong hàng chục regex.
    matched_pattern_name: Mapped[str | None] = mapped_column(String(120))

    category_slug: Mapped[str | None] = mapped_column(String(32))
    categorization_confidence: Mapped[float | None] = mapped_column(Float)

    is_potential_duplicate: Mapped[bool] = mapped_column(
        Boolean, nullable=False, server_default=text("false")
    )

    # Trỏ tới dòng pipeline_requests trước đó bị coi là trùng. Response trả về cho backend
    # là backend_request_id của dòng đó, không phải id này — backend chỉ tra được theo id
    # của chính nó.
    duplicate_of_request_id: Mapped[UUID | None] = mapped_column(UUID(as_uuid=True))

    classifier_version: Mapped[str | None] = mapped_column(String(32))
    extractor_version: Mapped[str | None] = mapped_column(String(32))
    categorizer_version: Mapped[str | None] = mapped_column(String(32))

    # Mẫu thô tương ứng trong raw_samples, khi việc thu thập đang bật. Thiếu liên kết
    # này thì feedback của người dùng ("danh mục đúng phải là X") không tìm lại được CÂU
    # đã sinh ra dự đoán, tức là không biến thành nhãn huấn luyện được.
    raw_sample_id: Mapped[UUID | None] = mapped_column(
        UUID(as_uuid=True), ForeignKey("raw_samples.id", ondelete="SET NULL")
    )

    processing_ms: Mapped[int | None] = mapped_column(Integer)
    error_message: Mapped[str | None] = mapped_column(Text)
    created_at: Mapped[created_at]

    __table_args__ = (
        UniqueConstraint("backend_request_id", name="uq_pipeline_requests_backend_id"),
        # Index của Duplicate Detector: lọc theo user rồi quét cửa sổ 5 phút.
        Index("idx_pipeline_requests_dedup", "user_id_hash", "transacted_at"),
        Index("idx_pipeline_requests_created_at", "created_at"),
        enum_check("pipeline_result", PipelineResult, "chk_pipeline_requests_result"),
        enum_check(
            "classifier_label",
            ClassifierLabel,
            "chk_pipeline_requests_classifier_label",
            nullable=True,
        ),
        enum_check(
            "transaction_type",
            TransactionType,
            "chk_pipeline_requests_transaction_type",
            nullable=True,
        ),
        enum_check(
            "extraction_method",
            ExtractionMethod,
            "chk_pipeline_requests_extraction_method",
            nullable=True,
        ),
        CheckConstraint(
            f"category_slug IS NULL OR category_slug IN ({_CATEGORY_LIST})",
            name="chk_pipeline_requests_category_slug",
        ),
    )


__all__ = ["PipelineRequest"]
