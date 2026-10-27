"""``nlu_candidates`` — câu chat mà bộ hiểu câu chưa chắc, chờ NGƯỜI duyệt gán nhãn rồi mới được vào ``nlu_examples`` (dữ liệu huấn luyện Rasa).

Vì sao là bảng riêng chứ không thêm cột vào ``nlu_examples``: câu của người dùng là dữ liệu cá nhân nên phải xóa được theo người (``user_id_hash``), còn
``nlu_examples`` có ``text_key`` duy nhất toàn cục và là dữ liệu đã duyệt, không thuộc về ai. ``predicted_intent`` chỉ là đoán của máy (Rasa + luật), KHÔNG phải nhãn:
nhãn luôn do người duyệt chọn. Câu được nhận thì chuyển sang ``nlu_examples`` rồi xóa khỏi đây (không còn gắn với người dùng); câu bị loại giữ vài ngày để không quay lại
ngay, rồi bị dọn. Không bao giờ log nội dung ``text``.
"""

from sqlalchemy import CheckConstraint, Float, Index, String, Text, UniqueConstraint
from sqlalchemy.orm import Mapped, mapped_column

from app.core.enums import NluCandidateStatus
from app.db.base import Base, created_at, enum_check, sha256_hex, uuid_pk


class NluCandidate(Base):
    __tablename__ = "nlu_candidates"

    id: Mapped[uuid_pk]
    user_id_hash: Mapped[sha256_hex]
    text: Mapped[str] = mapped_column(Text, nullable=False)
    text_key: Mapped[str] = mapped_column(String(400), nullable=False)
    predicted_intent: Mapped[str | None] = mapped_column(String(32))
    predicted_confidence: Mapped[float | None] = mapped_column(Float)
    unclear_reason: Mapped[str] = mapped_column(String(24), nullable=False)  # low_confidence | disagree | not_understood
    status: Mapped[str] = mapped_column(String(16), nullable=False, server_default=NluCandidateStatus.PENDING.value)
    created_at: Mapped[created_at]

    __table_args__ = (
        UniqueConstraint("user_id_hash", "text_key", name="uq_nlu_candidates_user_text"),
        Index("idx_nlu_candidates_status_created", "status", "created_at"),
        enum_check("status", NluCandidateStatus, "chk_nlu_candidates_status"),
        CheckConstraint("user_id_hash ~ '^[0-9a-f]{64}$'", name="chk_nlu_candidates_user_hash"),
    )


__all__ = ["NluCandidate"]
