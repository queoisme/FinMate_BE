"""``model_versions`` + ``training_jobs`` — registry model và lịch sử train.

Cơ chế promote nằm ở partial unique index ``uq_model_versions_active``: DB đảm bảo mỗi
stage có ĐÚNG một model ``active``. Không có index đó thì hai lần promote lỡ tay để lại
hai model cùng active và pipeline nạp cái nào là tuỳ thứ tự dòng trả về.
"""

from sqlalchemy import (
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

from app.core.enums import JobStatus, ModelStage, ModelStatus
from app.db.base import Base, created_at, enum_check, timestamptz, uuid_pk


class TrainingJob(Base):
    __tablename__ = "training_jobs"

    id: Mapped[uuid_pk]
    stage: Mapped[str] = mapped_column(String(32), nullable=False)
    status: Mapped[str] = mapped_column(String(16), nullable=False)
    params: Mapped[dict | None] = mapped_column(JSONB)
    sample_count: Mapped[int | None] = mapped_column(Integer)
    started_at: Mapped[timestamptz]
    finished_at: Mapped[timestamptz | None] = mapped_column()
    error_message: Mapped[str | None] = mapped_column(Text)
    created_at: Mapped[created_at]

    __table_args__ = (
        Index("idx_training_jobs_stage_created", "stage", "created_at"),
        enum_check("stage", ModelStage, "chk_training_jobs_stage"),
        enum_check("status", JobStatus, "chk_training_jobs_status"),
    )


class ModelVersion(Base):
    __tablename__ = "model_versions"

    id: Mapped[uuid_pk]
    stage: Mapped[str] = mapped_column(String(32), nullable=False)
    version: Mapped[str] = mapped_column(String(32), nullable=False)

    # Tương đối so với MODEL_REGISTRY_PATH, ví dụ "classifier/1.0.0/model.joblib".
    artifact_path: Mapped[str] = mapped_column(Text, nullable=False)

    status: Mapped[str] = mapped_column(String(16), nullable=False)
    metrics: Mapped[dict | None] = mapped_column(JSONB)
    training_job_id: Mapped[UUID | None] = mapped_column(
        UUID(as_uuid=True), ForeignKey("training_jobs.id", ondelete="SET NULL")
    )
    trained_at: Mapped[timestamptz | None] = mapped_column()
    notes: Mapped[str | None] = mapped_column(Text)
    created_at: Mapped[created_at]
    updated_at: Mapped[created_at]

    __table_args__ = (
        UniqueConstraint("stage", "version", name="uq_model_versions_stage_version"),
        Index(
            "uq_model_versions_active",
            "stage",
            unique=True,
            postgresql_where=text("status = 'active'"),
        ),
        enum_check("stage", ModelStage, "chk_model_versions_stage"),
        enum_check("status", ModelStatus, "chk_model_versions_status"),
    )


__all__ = ["ModelVersion", "TrainingJob"]
