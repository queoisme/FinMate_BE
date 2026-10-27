"""``ml_model_versions`` + ``ml_training_jobs`` — registry model và lịch sử train của MỌI model (sklearn của thông báo, model danh mục hóa đơn...).

Cơ chế promote nằm ở partial unique index ``uq_ml_model_versions_active``: DB đảm bảo mỗi model có ĐÚNG một bản ``active``. Không có index đó thì hai lần
promote lỡ tay để lại hai bản cùng active và pipeline nạp cái nào là tuỳ thứ tự dòng trả về.

Trước đây có hai bộ bảng (``model_versions``/``training_jobs`` cho thông báo, ``bill_model_versions``/``bill_training_jobs`` cho hóa đơn) phân biệt bằng cột
``stage``. Giờ cột đó là ``model_id`` trỏ ``ml_models``; ``ModelVersion.stage`` / ``TrainingJob.stage`` vẫn dùng được như thuộc tính (đọc ``ml_models.code``) và
trong ``WHERE``, nên mã cũ giữ nguyên. Tạo dòng mới thì truyền ``model_id`` (xem ``model_repo.model_id_for``).
"""

from sqlalchemy import (
    CheckConstraint,
    ForeignKey,
    Index,
    Integer,
    String,
    Text,
    UniqueConstraint,
    select,
    text,
)
from sqlalchemy.dialects.postgresql import JSONB, UUID
from sqlalchemy.ext.hybrid import hybrid_property
from sqlalchemy.orm import Mapped, mapped_column, relationship

from app.core.enums import JobStatus, ModelStatus
from app.db.base import Base, created_at, enum_check, timestamptz, uuid_pk
from app.db.models.ml_model import MlModel

_HEX64 = "~ '^[0-9a-f]{64}$'"


class TrainingJob(Base):
    __tablename__ = "ml_training_jobs"

    id: Mapped[uuid_pk]
    model_id: Mapped[UUID] = mapped_column(UUID(as_uuid=True), ForeignKey("ml_models.id", ondelete="RESTRICT"), nullable=False)
    status: Mapped[str] = mapped_column(String(16), nullable=False)
    params: Mapped[dict | None] = mapped_column(JSONB)
    dataset_hash: Mapped[str | None] = mapped_column(String(64))  # SHA-256 của tập dữ liệu đã dùng, để biết hai lần train có cùng dữ liệu không
    sample_count: Mapped[int | None] = mapped_column(Integer)
    started_at: Mapped[timestamptz]
    finished_at: Mapped[timestamptz | None] = mapped_column()
    error_message: Mapped[str | None] = mapped_column(Text)
    created_at: Mapped[created_at]

    model: Mapped[MlModel] = relationship(lazy="joined")

    @hybrid_property
    def stage(self) -> str:
        return self.model.code

    @stage.inplace.expression
    @classmethod
    def _stage_expression(cls):
        return select(MlModel.code).where(MlModel.id == cls.model_id).scalar_subquery()

    __table_args__ = (
        Index("idx_ml_training_jobs_model_created", "model_id", "created_at"),
        enum_check("status", JobStatus, "chk_training_jobs_status"),
        CheckConstraint("sample_count IS NULL OR sample_count >= 0", name="chk_ml_training_jobs_sample_count"),
        CheckConstraint(f"dataset_hash IS NULL OR dataset_hash {_HEX64}", name="chk_ml_training_jobs_dataset_hash"),
        CheckConstraint("finished_at IS NULL OR finished_at >= started_at", name="chk_ml_training_jobs_times"),
        CheckConstraint("params IS NULL OR jsonb_typeof(params) = 'object'", name="chk_ml_training_jobs_params"),
    )


class ModelVersion(Base):
    __tablename__ = "ml_model_versions"

    id: Mapped[uuid_pk]
    model_id: Mapped[UUID] = mapped_column(UUID(as_uuid=True), ForeignKey("ml_models.id", ondelete="RESTRICT"), nullable=False)
    version: Mapped[str] = mapped_column(String(32), nullable=False)

    # Tương đối so với MODEL_REGISTRY_PATH, ví dụ "classifier/1.0.0/model.joblib". Rỗng/NULL khi không có tệp (bản bị loại, hoặc model gọi qua API).
    artifact_path: Mapped[str | None] = mapped_column(Text)

    # SHA-256(system prompt + few-shot) của model LLM: hash lệch mà ``version`` không đổi nghĩa là ai đó sửa prompt quên tăng version.
    prompt_hash: Mapped[str | None] = mapped_column(String(64))
    status: Mapped[str] = mapped_column(String(16), nullable=False)
    metrics: Mapped[dict | None] = mapped_column(JSONB)
    training_job_id: Mapped[UUID | None] = mapped_column(
        UUID(as_uuid=True), ForeignKey("ml_training_jobs.id", ondelete="SET NULL")
    )
    trained_at: Mapped[timestamptz | None] = mapped_column()
    notes: Mapped[str | None] = mapped_column(Text)
    created_at: Mapped[created_at]
    updated_at: Mapped[created_at]

    model: Mapped[MlModel] = relationship(lazy="joined")

    @hybrid_property
    def stage(self) -> str:
        return self.model.code

    @stage.inplace.expression
    @classmethod
    def _stage_expression(cls):
        return select(MlModel.code).where(MlModel.id == cls.model_id).scalar_subquery()

    __table_args__ = (
        UniqueConstraint("model_id", "version", name="uq_ml_model_versions_model_version"),
        Index(
            "uq_ml_model_versions_active",
            "model_id",
            unique=True,
            postgresql_where=text("status = 'active'"),
        ),
        # FK training_job_id (ON DELETE SET NULL): xóa một lần huấn luyện không được quét cả bảng phiên bản.
        Index(
            "idx_model_versions_training_job",
            "training_job_id",
            postgresql_where=text("training_job_id IS NOT NULL"),
        ),
        enum_check("status", ModelStatus, "chk_ml_model_versions_status"),
        CheckConstraint(f"prompt_hash IS NULL OR prompt_hash {_HEX64}", name="chk_ml_model_versions_prompt_hash"),
    )


__all__ = ["ModelVersion", "TrainingJob"]
