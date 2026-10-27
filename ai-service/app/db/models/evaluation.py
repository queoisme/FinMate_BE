"""``ml_evaluation_runs`` + ``ml_evaluation_category_metrics`` + ``ml_evaluation_predictions`` — đánh giá của MỌI model (thông báo và hóa đơn).

Một model chỉ được promote sau khi có ít nhất một evaluation run trên split ``test``.
``evaluation_predictions`` giữ từng dự đoán để đọc lại đúng những câu model đoán sai —
accuracy tổng không nói cho ai biết nó hỏng ở danh mục nào.
"""

from sqlalchemy import (
    Boolean,
    CheckConstraint,
    Float,
    ForeignKey,
    Index,
    Integer,
    String,
    Text,
    UniqueConstraint,
)
from sqlalchemy.dialects.postgresql import JSONB, UUID
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, created_at, uuid_pk


class EvaluationRun(Base):
    __tablename__ = "ml_evaluation_runs"

    id: Mapped[uuid_pk]
    model_version_id: Mapped[UUID] = mapped_column(
        UUID(as_uuid=True),
        ForeignKey("ml_model_versions.id", ondelete="CASCADE"),
        nullable=False,
    )
    split: Mapped[str] = mapped_column(String(16), nullable=False)
    sample_count: Mapped[int] = mapped_column(Integer, nullable=False)

    # Float ở đây là điểm số, không phải tiền — lệnh cấm float của AGENTS.md §3.2 nhắm
    # vào amount_cents.
    accuracy: Mapped[float] = mapped_column(Float, nullable=False)
    macro_f1: Mapped[float] = mapped_column(Float, nullable=False)

    metrics: Mapped[dict | None] = mapped_column(JSONB)
    notes: Mapped[str | None] = mapped_column(Text)
    created_at: Mapped[created_at]

    __table_args__ = (
        Index("idx_evaluation_runs_model_version", "model_version_id", "created_at"),
        # train/val/test là ba tập của pipeline thông báo; "cv" là kiểm tra chéo theo cửa hàng của model hóa đơn (chưa có tập test riêng).
        CheckConstraint("split IN ('train', 'val', 'test', 'cv')", name="chk_ml_evaluation_runs_split"),
        CheckConstraint("accuracy BETWEEN 0 AND 1 AND macro_f1 BETWEEN 0 AND 1", name="chk_ml_evaluation_runs_scores"),
        CheckConstraint("sample_count >= 0", name="chk_ml_evaluation_runs_sample_count"),
    )


class EvaluationCategoryMetric(Base):
    __tablename__ = "ml_evaluation_category_metrics"

    id: Mapped[uuid_pk]
    evaluation_run_id: Mapped[UUID] = mapped_column(
        UUID(as_uuid=True),
        ForeignKey("ml_evaluation_runs.id", ondelete="CASCADE"),
        nullable=False,
    )

    # category_slug với Categorizer, classifier label với Classifier.
    label: Mapped[str] = mapped_column(String(64), nullable=False)
    precision: Mapped[float] = mapped_column(Float, nullable=False)
    recall: Mapped[float] = mapped_column(Float, nullable=False)
    f1: Mapped[float] = mapped_column(Float, nullable=False)
    support: Mapped[int] = mapped_column(Integer, nullable=False)

    __table_args__ = (
        UniqueConstraint("evaluation_run_id", "label", name="uq_eval_category_metrics"),
        CheckConstraint(
            '"precision" BETWEEN 0 AND 1 AND recall BETWEEN 0 AND 1 AND f1 BETWEEN 0 AND 1', name="chk_ml_evaluation_category_metrics_scores"
        ),
        CheckConstraint("support >= 0", name="chk_ml_evaluation_category_metrics_support"),
    )


class EvaluationPrediction(Base):
    __tablename__ = "ml_evaluation_predictions"

    id: Mapped[uuid_pk]
    evaluation_run_id: Mapped[UUID] = mapped_column(
        UUID(as_uuid=True),
        ForeignKey("ml_evaluation_runs.id", ondelete="CASCADE"),
        nullable=False,
    )
    labeled_sample_id: Mapped[UUID] = mapped_column(
        UUID(as_uuid=True),
        ForeignKey("ml_labeled_samples.id", ondelete="CASCADE"),
        nullable=False,
    )
    predicted_label: Mapped[str] = mapped_column(String(64), nullable=False)
    true_label: Mapped[str] = mapped_column(String(64), nullable=False)
    confidence: Mapped[float] = mapped_column(Float, nullable=False)
    is_correct: Mapped[bool] = mapped_column(Boolean, nullable=False)

    __table_args__ = (
        Index("idx_evaluation_predictions_run", "evaluation_run_id", "is_correct"),
        # FK labeled_sample_id (ON DELETE CASCADE): xóa một mẫu có nhãn kéo theo xóa các dự đoán của nó, cần chỉ mục này để tìm chúng.
        Index("idx_evaluation_predictions_labeled_sample", "labeled_sample_id"),
        # Một lần đánh giá chỉ chấm mỗi mẫu một lần: chấm lại hai lần làm accuracy tính trên dữ liệu trùng.
        UniqueConstraint("evaluation_run_id", "labeled_sample_id", name="uq_ml_evaluation_predictions_run_sample"),
        CheckConstraint("confidence BETWEEN 0 AND 1", name="chk_ml_evaluation_predictions_confidence"),
    )


__all__ = ["EvaluationCategoryMetric", "EvaluationPrediction", "EvaluationRun"]
