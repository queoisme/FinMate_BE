"""``ml_label_events`` — nhật ký NHÃN của mẫu, chỉ ghi thêm (không bao giờ UPDATE): mỗi lần nhãn của một mẫu được đặt hoặc đổi là một dòng mới.

``ml_labeled_samples`` giữ nhãn HIỆN TẠI (để truy vấn huấn luyện đơn giản); bảng này giữ LỊCH SỬ nên trả lời được "nhãn này do ai đặt, dựa trên phản hồi nào, trước đó là gì".
Nhãn hiện tại phải bằng sự kiện mới nhất của mẫu đó: mọi nơi ghi nhãn gọi ``sample_repo.add_label_event`` (và ocr-service ghi thẳng SQL), nhãn cũ được chép thành sự kiện lúc migration 0024.

UPDATE và DELETE trực tiếp bị trigger chặn (``app/db/ml_sql.py``); thay đổi dây chuyền do khóa ngoại vẫn chạy (xóa mẫu có nhãn xóa sự kiện của nó, xóa phản hồi đặt ``feedback_id`` về NULL), vì quyền xóa dữ liệu của người dùng không được bị bảng này cản.
"""

from sqlalchemy import CheckConstraint, ForeignKey, Index, String, text
from sqlalchemy.dialects.postgresql import UUID
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, created_at, uuid_pk
from app.db.models.category import category_fk

LABEL_EVENT_SOURCES = ("feedback", "gold", "import", "user", "system")


class MlLabelEvent(Base):
    __tablename__ = "ml_label_events"

    id: Mapped[uuid_pk]
    labeled_sample_id: Mapped[UUID] = mapped_column(
        UUID(as_uuid=True), ForeignKey("ml_labeled_samples.id", ondelete="CASCADE"), nullable=False
    )
    category_slug: Mapped[str | None] = mapped_column(String(32), category_fk())
    is_financial: Mapped[bool] = mapped_column(nullable=False)

    # feedback = phản hồi người dùng qua feedback_batch | gold = người soát tay | import = nạp từ bộ có sẵn (seed, nhập tay) | user = người dùng đặt trực tiếp | system = máy sinh.
    source: Mapped[str] = mapped_column(String(16), nullable=False)
    feedback_id: Mapped[UUID | None] = mapped_column(UUID(as_uuid=True), ForeignKey("ml_user_feedback.id", ondelete="SET NULL"))
    actor_hash: Mapped[str | None] = mapped_column(String(64))  # SHA-256 của người đặt nhãn (không phải id thật); NULL với nhãn do máy hoặc bộ có sẵn
    created_at: Mapped[created_at]

    __table_args__ = (
        # Lấy nhãn mới nhất của một mẫu: quét ngược chỉ mục theo (mẫu, thời gian).
        Index("idx_ml_label_events_sample_created", "labeled_sample_id", "created_at"),
        # FK feedback_id (ON DELETE SET NULL): xóa một phản hồi không được quét cả bảng sự kiện.
        Index("idx_ml_label_events_feedback", "feedback_id", postgresql_where=text("feedback_id IS NOT NULL")),
        CheckConstraint(f"source IN ({', '.join(repr(s) for s in LABEL_EVENT_SOURCES)})", name="chk_ml_label_events_source"),
        # Mẫu không phải giao dịch thì không mang danh mục (cùng quy tắc với ml_labeled_samples).
        CheckConstraint("is_financial OR category_slug IS NULL", name="chk_ml_label_events_shape"),
        CheckConstraint("actor_hash IS NULL OR actor_hash ~ '^[0-9a-f]{64}$'", name="chk_ml_label_events_actor_hash"),
    )


__all__ = ["LABEL_EVENT_SOURCES", "MlLabelEvent"]
