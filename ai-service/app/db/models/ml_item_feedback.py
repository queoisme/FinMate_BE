"""``ml_item_feedback`` — phiếu của người dùng cho danh mục của MỘT MÓN hàng: mỗi người một phiếu cho mỗi món (khóa chính ``(item_key, user_id_hash)``).

``v_item_consensus`` đếm số người cùng chọn một danh mục cho một món; đủ đồng thuận thì món được đưa vào ``kb_item_categories`` (``source = 'consensus'``).
``item_key`` là tham chiếu LOGIC tới ``kb_item_categories.item_key``, không có khóa ngoại: món có thể chưa có trong bảng kiến thức, và chính phiếu bầu là cách nó vào đó.
Chưa có chỗ nào trong code ghi bảng này (API nhận phản hồi theo món chưa làm).
"""

from sqlalchemy import CheckConstraint, Index, String, Text
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, created_at
from app.db.models.category import category_fk


class MlItemFeedback(Base):
    __tablename__ = "ml_item_feedback"

    item_key: Mapped[str] = mapped_column(Text, primary_key=True)  # tên món đã bỏ dấu, chữ thường, bỏ số tiền (cùng chuẩn hóa với kb_item_categories)
    user_id_hash: Mapped[str] = mapped_column(String(64), primary_key=True)
    category_slug: Mapped[str] = mapped_column(String(32), category_fk(), nullable=False)
    created_at: Mapped[created_at]

    __table_args__ = (
        # Cho view đồng thuận: gom theo (món, danh mục).
        Index("idx_ml_item_feedback_item_category", "item_key", "category_slug"),
        CheckConstraint("char_length(item_key) BETWEEN 3 AND 60", name="chk_ml_item_feedback_key_len"),
        CheckConstraint("item_key = lower(item_key)", name="chk_ml_item_feedback_key_lower"),
        CheckConstraint("user_id_hash ~ '^[0-9a-f]{64}$'", name="chk_ml_item_feedback_user_hash"),
        # Loại danh mục (chỉ khoản chi, không dòng bí danh) do trigger ``app.db.category_guard`` kiểm: thêm danh mục không phải sửa CHECK.
    )


__all__ = ["MlItemFeedback"]
