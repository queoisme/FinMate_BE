"""``evaluation_runs`` + ``evaluation_category_metrics`` + ``evaluation_predictions``.

Một model chỉ được promote sau khi có ít nhất một evaluation run trên split ``test``.
``evaluation_predictions`` giữ từng dự đoán để đọc lại đúng những câu model đoán sai —
accuracy tổng không nói cho ai biết nó hỏng ở danh mục nào.
"""

from sqlalchemy import (
    Boolean,
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

from app.core.enums import SplitName
from app.db.base import Base, created_at, enum_check, uuid_pk


class EvaluationRun(Base):
    __tablename__ = "evaluation_runs"

    id: Mapped[uuid_pk]
    model_version_id: Mapped[UUID] = mapped_column(
        UUID(as_uuid=True),
        ForeignKey("model_versions.id", ondelete="CASCADE"),
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
        enum_check("split", SplitName, "chk_evaluation_runs_split"),
    )


class EvaluationCategoryMetric(Base):
    __tablename__ = "evaluation_category_metrics"

    id: Mapped[uuid_pk]
    evaluation_run_id: Mapped[UUID] = mapped_column(
        UUID(as_uuid=True),
        ForeignKey("evaluation_runs.id", ondelete="CASCADE"),
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
    )


class EvaluationPrediction(Base):
    __tablename__ = "evaluation_predictions"

    id: Mapped[uuid_pk]
    evaluation_run_id: Mapped[UUID] = mapped_column(
        UUID(as_uuid=True),
        ForeignKey("evaluation_runs.id", ondelete="CASCADE"),
        nullable=False,
    )
    labeled_sample_id: Mapped[UUID] = mapped_column(
        UUID(as_uuid=True),
        ForeignKey("labeled_samples.id", ondelete="CASCADE"),
        nullable=False,
    )
    predicted_label: Mapped[str] = mapped_column(String(64), nullable=False)
    true_label: Mapped[str] = mapped_column(String(64), nullable=False)
    confidence: Mapped[float] = mapped_column(Float, nullable=False)
    is_correct: Mapped[bool] = mapped_column(Boolean, nullable=False)

    __table_args__ = (
        Index("idx_evaluation_predictions_run", "evaluation_run_id", "is_correct"),
    )


__all__ = ["EvaluationCategoryMetric", "EvaluationPrediction", "EvaluationRun"]
