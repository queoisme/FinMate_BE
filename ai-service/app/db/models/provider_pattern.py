"""``provider_patterns`` — regex bóc trường từ thông báo, tra theo ``package_name``.

Mỗi dòng là MỘT regex phủ cả template, dùng named group (``amount``, ``sign``,
``merchant``, ``balance``, ``occurred_at``) chứ không phải một regex cho mỗi trường:
tách rời thì hai template khác nhau của cùng một ngân hàng có thể khớp chéo group của
nhau và ghép ra một giao dịch không tồn tại.
"""

from sqlalchemy import Boolean, Index, Integer, String, Text, UniqueConstraint, text
from sqlalchemy.orm import Mapped, mapped_column

from app.core.enums import TransactionType
from app.db.base import Base, created_at, enum_check, long_text, short_text, uuid_pk


class ProviderPattern(Base):
    __tablename__ = "provider_patterns"

    id: Mapped[uuid_pk]
    provider_key: Mapped[short_text]
    package_name: Mapped[short_text]
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
        UniqueConstraint(
            "package_name", "pattern_name", name="uq_provider_patterns_name"
        ),
        Index(
            "idx_provider_patterns_lookup",
            "package_name",
            "is_active",
            "priority",
        ),
        enum_check(
            "transaction_type",
            TransactionType,
            "chk_provider_patterns_transaction_type",
            nullable=True,
        ),
    )


__all__ = ["ProviderPattern"]
