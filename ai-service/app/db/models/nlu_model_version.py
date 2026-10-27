"""``nlu_model_versions`` — sổ đăng ký model Rasa NLU: học từ bản dữ liệu nào, đo ra sao, bản nào đang chạy.

Tách khỏi ``model_versions`` (sklearn): bảng đó bị CHECK theo ``ModelStage`` và các vòng lặp nạp model sẽ cố ``joblib.load`` một tệp Rasa .tar.gz.
Mỗi model ghi ``dataset_hash`` (sha256 của tập train xuất từ ``nlu_examples``) để biết model học từ bản dữ liệu nào, và hai bộ số đo trên tập test của DB:
``eval_raw`` (Rasa thô) và ``eval_final`` (Rasa + lớp luật) — hai con số khác nhau, luật che lỗi của model nên không được gộp.
Partial unique index ``uq_nlu_model_versions_active`` đảm bảo chỉ MỘT model ``active`` (cùng cách ``model_versions``).
"""

from sqlalchemy import CheckConstraint, Index, String, Text, UniqueConstraint, text
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.orm import Mapped, mapped_column

from app.core.enums import NluModelStatus
from app.db.base import Base, created_at, enum_check, timestamptz, uuid_pk


class NluModelVersion(Base):
    __tablename__ = "nlu_model_versions"

    id: Mapped[uuid_pk]
    version: Mapped[str] = mapped_column(String(64), nullable=False)  # tên tệp không đuôi: finmate-nlu-v12
    artifact_path: Mapped[str] = mapped_column(Text, nullable=False)  # tương đối so với rasa/ (models/finmate-nlu-v12.tar.gz)
    status: Mapped[str] = mapped_column(String(16), nullable=False)
    dataset_hash: Mapped[str] = mapped_column(String(64), nullable=False)  # "unknown" cho model có từ trước khi có sổ này
    dataset_counts: Mapped[dict | None] = mapped_column(JSONB)  # {"train": {"add_expense": 1181, ...}, "test": {...}}
    eval_raw: Mapped[dict | None] = mapped_column(JSONB)
    eval_final: Mapped[dict | None] = mapped_column(JSONB)
    notes: Mapped[str | None] = mapped_column(Text)
    created_at: Mapped[created_at]
    activated_at: Mapped[timestamptz | None] = mapped_column()

    __table_args__ = (
        UniqueConstraint("version", name="uq_nlu_model_versions_version"),
        Index("uq_nlu_model_versions_active", "status", unique=True, postgresql_where=text("status = 'active'")),
        enum_check("status", NluModelStatus, "chk_nlu_model_versions_status"),
        CheckConstraint("dataset_hash = 'unknown' OR dataset_hash ~ '^[0-9a-f]{64}$'", name="chk_nlu_model_versions_dataset_hash"),
    )


__all__ = ["NluModelVersion"]
