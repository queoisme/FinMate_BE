"""``user_feedback`` + ``feedback_batch_jobs`` — vòng phản hồi từ người dùng về dataset.

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

from app.core.enums import CATEGORY_SLUGS, FeedbackType, JobStatus
from app.db.base import Base, created_at, enum_check, short_text, timestamptz, uuid_pk

_CATEGORY_LIST = ", ".join(f"'{slug}'" for slug in CATEGORY_SLUGS)


class FeedbackBatchJob(Base):
    __tablename__ = "feedback_batch_jobs"

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
    )


class UserFeedback(Base):
    __tablename__ = "user_feedback"

    id: Mapped[uuid_pk]
    backend_transaction_id_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    user_id_hash: Mapped[str] = mapped_column(String(64), nullable=False)

    # Contract §3.3 gọi là pipeline_request_id nhưng giá trị backend gửi là id
    # notification_log của nó, tức là khớp với pipeline_requests.backend_request_id.
    # Nullable vì backend hiện gửi null — xem ghi chú ở feedback_repo.
    pipeline_request_id: Mapped[UUID | None] = mapped_column(UUID(as_uuid=True))

    package_name: Mapped[short_text]
    predicted_category: Mapped[str | None] = mapped_column(String(32))
    corrected_category: Mapped[str | None] = mapped_column(String(32))
    feedback_type: Mapped[str] = mapped_column(String(32), nullable=False)

    processed_at: Mapped[timestamptz | None] = mapped_column()
    batch_job_id: Mapped[UUID | None] = mapped_column(
        UUID(as_uuid=True), ForeignKey("feedback_batch_jobs.id", ondelete="SET NULL")
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
        enum_check("feedback_type", FeedbackType, "chk_user_feedback_type"),
        CheckConstraint(
            f"predicted_category IS NULL OR predicted_category IN ({_CATEGORY_LIST})",
            name="chk_user_feedback_predicted_category",
        ),
        CheckConstraint(
            f"corrected_category IS NULL OR corrected_category IN ({_CATEGORY_LIST})",
            name="chk_user_feedback_corrected_category",
        ),
    )


__all__ = ["FeedbackBatchJob", "UserFeedback"]
