"""``ml_models`` — danh mục các model (một dòng mỗi model), thay cho cột ``stage`` dạng chuỗi tự do của ``model_versions`` / ``training_jobs``.

``code`` giữ đúng giá trị ``stage`` cũ (``classifier``, ``categorizer``, ``bill_slug``, ``bill_category``) nên mã gọi và ocr-service không phải đổi tên.
Phiên bản, lần huấn luyện và lần đánh giá của MỌI model nằm chung ở ``ml_model_versions`` / ``ml_training_jobs`` / ``ml_evaluation_*``.
Model Rasa NLU vẫn ở bảng riêng ``nlu_model_versions`` (vòng đời và artifact khác: cả thư mục .tar.gz do Rasa nạp).
"""

from sqlalchemy import CheckConstraint, String, Text, UniqueConstraint
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, created_at, uuid_pk

MODEL_KINDS = ("sklearn", "rasa", "llm", "rules")


class MlModel(Base):
    __tablename__ = "ml_models"

    id: Mapped[uuid_pk]
    code: Mapped[str] = mapped_column(String(48), nullable=False)
    kind: Mapped[str] = mapped_column(String(16), nullable=False)
    provider: Mapped[str | None] = mapped_column(String(32))
    notes: Mapped[str | None] = mapped_column(Text)
    created_at: Mapped[created_at]

    __table_args__ = (
        UniqueConstraint("code", name="uq_ml_models_code"),
        CheckConstraint("kind IN ('sklearn', 'rasa', 'llm', 'rules')", name="chk_ml_models_kind"),
    )


__all__ = ["MODEL_KINDS", "MlModel"]
