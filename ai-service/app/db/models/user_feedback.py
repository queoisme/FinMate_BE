"""``ml_user_feedback`` + ``ml_feedback_batches`` — vòng phản hồi từ người dùng về dataset.

Người dùng sửa danh mục của một giao dịch trên app → backend
(``UpdateTransactionCommandHandler``) gọi ``POST /api/v1/feedback`` → dòng ở đây →
``scripts/feedback_batch.py`` biến thành ``labeled_samples`` → lần train sau model học
được. Đây là chỗ khép vòng, không phải bảng lưu trữ chết.
"""

from sqlalchemy import (
    CheckConstraint,
    ForeignKey,
    Index,
    Integer,
    String,
    Text,
    text,
)
from sqlalchemy.dialects.postgresql import UUID
from sqlalchemy.orm import Mapped, mapped_column

from app.core.enums import FeedbackType, JobStatus
from app.db.base import Base, created_at, enum_check, short_text, timestamptz, uuid_pk
from app.db.models.category import category_fk


class FeedbackBatchJob(Base):
    __tablename__ = "ml_feedback_batches"

    id: Mapped[uuid_pk]
    status: Mapped[str] = mapped_column(String(16), nullable=False)
    started_at: Mapped[timestamptz]
    finished_at: Mapped[timestamptz | None] = mapped_column()
    feedback_count: Mapped[int] = mapped_column(
        Integer, nullable=False, server_default=text("0")
    )
    created_labeled_samples: Mapped[int] = mapped_column(
        Integer, nullable=False, server_default=text("0")
    )
    error_message: Mapped[str | None] = mapped_column(Text)
    created_at: Mapped[created_at]

    __table_args__ = (
        enum_check("status", JobStatus, "chk_feedback_batch_jobs_status"),
        CheckConstraint("feedback_count >= 0 AND created_labeled_samples >= 0", name="chk_ml_feedback_batches_counts"),
        CheckConstraint("finished_at IS NULL OR finished_at >= started_at", name="chk_ml_feedback_batches_times"),
    )


class UserFeedback(Base):
    __tablename__ = "ml_user_feedback"

    id: Mapped[uuid_pk]
    backend_transaction_id_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    user_id_hash: Mapped[str] = mapped_column(String(64), nullable=False)

    # Contract §3.3 gọi là pipeline_request_id nhưng giá trị backend gửi là id
    # notification_log của nó, tức là khớp với ops_analyze_log.backend_request_id.
    # Nullable vì backend hiện gửi null — xem ghi chú ở feedback_repo.
    pipeline_request_id: Mapped[UUID | None] = mapped_column(UUID(as_uuid=True))

    package_name: Mapped[short_text]
    predicted_category: Mapped[str | None] = mapped_column(String(32), category_fk())
    corrected_category: Mapped[str | None] = mapped_column(String(32), category_fk())
    feedback_type: Mapped[str] = mapped_column(String(32), nullable=False)

    processed_at: Mapped[timestamptz | None] = mapped_column()
    batch_id: Mapped[UUID | None] = mapped_column(
        UUID(as_uuid=True), ForeignKey("ml_feedback_batches.id", ondelete="SET NULL")
    )
    created_at: Mapped[created_at]

    __table_args__ = (
        # Quét "feedback chưa xử lý" là truy vấn duy nhất của feedback_batch.
        Index(
            "idx_user_feedback_unprocessed",
            "created_at",
            postgresql_where=text("processed_at IS NULL"),
        ),
        Index("idx_user_feedback_pipeline_request", "pipeline_request_id"),
        # FK batch_id (ON DELETE SET NULL): xóa một lô xử lý không được quét cả bảng phản hồi.
        Index(
            "idx_user_feedback_batch_job",
            "batch_id",
            postgresql_where=text("batch_id IS NOT NULL"),
        ),
        enum_check("feedback_type", FeedbackType, "chk_user_feedback_type"),
        CheckConstraint("user_id_hash ~ '^[0-9a-f]{64}$'", name="chk_ml_user_feedback_user_hash"),
        CheckConstraint("backend_transaction_id_hash ~ '^[0-9a-f]{64}$'", name="chk_ml_user_feedback_txn_hash"),
    )


__all__ = ["FeedbackBatchJob", "UserFeedback"]
