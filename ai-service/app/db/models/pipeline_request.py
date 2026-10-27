"""``ops_analyze_log`` — nhật ký mọi lần ``POST /api/v1/analyze`` chạy.

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
from sqlalchemy.dialects.postgresql import JSONB, UUID
from sqlalchemy.orm import Mapped, mapped_column

from app.core.enums import ClassifierLabel, PipelineResult
from app.db.base import Base, created_at, enum_check, short_text, timestamptz, uuid_pk
from app.db.models.category import category_fk


class PipelineRequest(Base):
    __tablename__ = "ops_analyze_log"

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

    category_slug: Mapped[str | None] = mapped_column(String(32), category_fk())
    categorization_confidence: Mapped[float | None] = mapped_column(Float)

    # timeout | bad_json | out_of_taxonomy: vì sao bộ phân loại danh mục (Gemini) phải lùi về luật.
    categorizer_fallback_reason: Mapped[str | None] = mapped_column(String(32))

    is_potential_duplicate: Mapped[bool] = mapped_column(
        Boolean, nullable=False, server_default=text("false")
    )

    # Trỏ tới dòng ops_analyze_log trước đó bị coi là trùng. Response trả về cho backend
    # là backend_request_id của dòng đó, không phải id này — backend chỉ tra được theo id
    # của chính nó.
    # Tự tham chiếu: dọn dòng cũ (scripts/purge_pipeline_requests.py) không để con trỏ treo.
    duplicate_of_request_id: Mapped[UUID | None] = mapped_column(
        UUID(as_uuid=True), ForeignKey("ops_analyze_log.id", ondelete="SET NULL")
    )

    classifier_version: Mapped[str | None] = mapped_column(String(32))
    extractor_version: Mapped[str | None] = mapped_column(String(32))
    categorizer_version: Mapped[str | None] = mapped_column(String(32))

    # Dòng kb_provider_patterns đã khớp. SET NULL: xóa pattern không được xóa lịch sử phân tích.
    pattern_id: Mapped[UUID | None] = mapped_column(UUID(as_uuid=True), ForeignKey("kb_provider_patterns.id", ondelete="SET NULL", name="fk_ops_analyze_log_pattern"))
    # Phiên bản cấu hình và chỉ dẫn đã dùng cho dòng này, để lần ngược vì sao hai lần chạy cho kết quả khác nhau.
    # none_as_null: gán None phải thành SQL NULL, không phải JSON "null" (JSON "null" vi phạm CHECK jsonb_typeof = object).
    config_versions: Mapped[dict | None] = mapped_column(JSONB(none_as_null=True))
    # Trường động của định dạng nhà cung cấp (không nằm trong sáu trường hợp đồng). CẤM chứa số tài khoản, thẻ, số điện thoại.
    extra_fields: Mapped[dict | None] = mapped_column(JSONB(none_as_null=True))

    # Mẫu thô tương ứng trong ml_raw_samples, khi việc thu thập đang bật. Thiếu liên kết
    # này thì feedback của người dùng ("danh mục đúng phải là X") không tìm lại được CÂU
    # đã sinh ra dự đoán, tức là không biến thành nhãn huấn luyện được.
    raw_sample_id: Mapped[UUID | None] = mapped_column(
        UUID(as_uuid=True), ForeignKey("ml_raw_samples.id", ondelete="SET NULL")
    )

    processing_ms: Mapped[int | None] = mapped_column(Integer)
    error_message: Mapped[str | None] = mapped_column(Text)
    created_at: Mapped[created_at]

    __table_args__ = (
        UniqueConstraint("backend_request_id", name="uq_pipeline_requests_backend_id"),
        # Index của Duplicate Detector: lọc theo user rồi quét cửa sổ 5 phút.
        Index("idx_pipeline_requests_dedup", "user_id_hash", "transacted_at"),
        Index("idx_pipeline_requests_created_at", "created_at"),
        # FK raw_sample_id (ON DELETE SET NULL): thiếu chỉ mục này thì mỗi mẫu thô bị xóa làm Postgres quét cả bảng
        # để tìm dòng con. Chỉ mục từng phần vì cột này NULL ở đa số dòng (chỉ có khi COLLECT_RAW_SAMPLES bật).
        Index(
            "idx_pipeline_requests_raw_sample",
            "raw_sample_id",
            postgresql_where=text("raw_sample_id IS NOT NULL"),
        ),
        enum_check("pipeline_result", PipelineResult, "chk_pipeline_requests_result"),
        enum_check(
            "classifier_label",
            ClassifierLabel,
            "chk_pipeline_requests_classifier_label",
            nullable=True,
        ),
        # transaction_type và extraction_method cố ý KHÔNG có CHECK (F1-09): định dạng nhà cung cấp có thể mang giá trị mới; ERD chỉ đóng tập cho pipeline_result và classifier_label.
        CheckConstraint("user_id_hash ~ '^[0-9a-f]{64}$'", name="chk_ops_analyze_log_user_hash"),
        CheckConstraint(
            "categorizer_fallback_reason IS NULL OR categorizer_fallback_reason IN ('timeout', 'bad_json', 'out_of_taxonomy')",
            name="chk_ops_analyze_log_fallback_reason",
        ),
        CheckConstraint("classifier_confidence IS NULL OR classifier_confidence BETWEEN 0 AND 1", name="chk_ops_analyze_log_classifier_conf"),
        CheckConstraint("extraction_confidence IS NULL OR extraction_confidence BETWEEN 0 AND 1", name="chk_ops_analyze_log_extraction_conf"),
        CheckConstraint(
            "categorization_confidence IS NULL OR categorization_confidence BETWEEN 0 AND 1", name="chk_ops_analyze_log_categorization_conf"
        ),
        CheckConstraint("amount_cents IS NULL OR amount_cents >= 0", name="chk_ops_analyze_log_amount"),
        CheckConstraint("processing_ms IS NULL OR processing_ms >= 0", name="chk_ops_analyze_log_processing_ms"),
        CheckConstraint("extra_fields IS NULL OR jsonb_typeof(extra_fields) = 'object'", name="chk_ops_analyze_log_extra_fields"),
        # FK tự tham chiếu SET NULL: xóa dòng cha không được quét cả bảng (cùng lý do với idx_pipeline_requests_raw_sample).
        Index("idx_ops_analyze_log_duplicate_of", "duplicate_of_request_id", postgresql_where=text("duplicate_of_request_id IS NOT NULL")),
        # FK pattern_id SET NULL: xóa một pattern không được quét cả bảng nhật ký.
        Index("idx_ops_analyze_log_pattern", "pattern_id", postgresql_where=text("pattern_id IS NOT NULL")),
    )


__all__ = ["PipelineRequest"]
