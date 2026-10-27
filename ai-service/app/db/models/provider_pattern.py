"""``kb_provider_patterns`` — regex bóc trường từ thông báo của MỘT định dạng (``kb_provider_formats``, nơi duy nhất giữ ``provider_key`` và ``package_name``).

Mỗi dòng là MỘT regex phủ cả template, dùng named group (``amount``, ``sign``,
``merchant``, ``balance``, ``occurred_at``) chứ không phải một regex cho mỗi trường:
tách rời thì hai template khác nhau của cùng một ngân hàng có thể khớp chéo group của
nhau và ghép ra một giao dịch không tồn tại.
"""

from sqlalchemy import Boolean, CheckConstraint, ForeignKey, Index, Integer, String, Text, UniqueConstraint, text
from sqlalchemy.dialects.postgresql import UUID
from sqlalchemy.orm import Mapped, mapped_column

from app.core.enums import TransactionType
from app.db.base import Base, created_at, enum_check, long_text, short_text, uuid_pk


class ProviderPattern(Base):
    __tablename__ = "kb_provider_patterns"

    id: Mapped[uuid_pk]
    # Mỗi regex thuộc về MỘT phiên bản định dạng (kb_provider_formats). RESTRICT: không xóa được định dạng còn regex.
    format_id: Mapped[UUID] = mapped_column(UUID(as_uuid=True), ForeignKey("kb_provider_formats.id", ondelete="RESTRICT"), nullable=False)
    pattern_name: Mapped[short_text]
    regex: Mapped[long_text]

    # NULL = suy chiều tiền từ named group ``sign`` của chính regex.
    transaction_type: Mapped[str | None] = mapped_column(String(16))

    # Thử tăng dần: template hẹp phải đứng trước template rộng, nếu không template rộng
    # nuốt hết và các trường chi tiết không bao giờ được bóc ra.
    priority: Mapped[int] = mapped_column(
        Integer, nullable=False, server_default=text("100")
    )

    # Mẫu mà regex này được viết ra từ đó. Có một test duyệt mọi pattern is_active và
    # assert nó còn khớp chính sample_text của mình — sửa regex làm hỏng template cũ sẽ
    # đỏ ngay ở CI thay vì đợi tới lúc user mất giao dịch.
    sample_text: Mapped[str | None] = mapped_column(Text)

    is_active: Mapped[bool] = mapped_column(
        Boolean, nullable=False, server_default=text("true")
    )
    notes: Mapped[str | None] = mapped_column(Text)
    created_at: Mapped[created_at]
    updated_at: Mapped[created_at]

    __table_args__ = (
        # Hai phiên bản của một định dạng được phép có cùng tên pattern (bản mới là bản sao đã sửa), nên tên chỉ duy nhất TRONG một định dạng.
        UniqueConstraint("format_id", "pattern_name", name="uq_kb_provider_patterns_format_name"),
        enum_check(
            "transaction_type",
            TransactionType,
            "chk_provider_patterns_transaction_type",
            nullable=True,
        ),
        CheckConstraint("priority >= 0", name="chk_kb_provider_patterns_priority"),
        # Nạp pattern của một định dạng theo thứ tự thử.
        Index("idx_kb_provider_patterns_format", "format_id", "is_active", "priority"),
    )


__all__ = ["ProviderPattern"]
