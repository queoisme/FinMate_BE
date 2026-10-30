"""``secret_store`` — key của bên thứ ba (Gemini, OpenRouter) do admin nhập qua trang quản trị, lưu ĐÃ MÃ HÓA (AES-256-GCM).

Mỗi tên một dòng, sửa tại chỗ: key cũ bị ghi đè vì không có lý do để giữ lại một key đã thay (nó thường đã bị thu hồi). Lịch sử AI ĐỔI key nằm ở ``kb_audit_log``
(``resource = 'secret'``), chỉ ghi tên, hành động, 4 ký tự cuối và lý do, không bao giờ ghi giá trị. Khóa chủ KHÔNG ở trong DB (biến môi trường ``SECRETS_MASTER_KEY``): lộ DB
mà không lộ khóa chủ thì key vẫn an toàn. ``name`` được đưa vào dữ liệu xác thực kèm (AAD) của GCM nên một dòng bị chép sang tên khác sẽ không giải mã được."""

from sqlalchemy import CheckConstraint, LargeBinary, SmallInteger, String, text
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, created_at, timestamptz


class SecretRow(Base):
    __tablename__ = "secret_store"

    name: Mapped[str] = mapped_column(String(48), primary_key=True)  # gemini_api_key | openrouter_api_key (danh sách do code quy định)
    ciphertext: Mapped[bytes] = mapped_column(LargeBinary, nullable=False)
    nonce: Mapped[bytes] = mapped_column(LargeBinary, nullable=False)  # 12 byte ngẫu nhiên MỖI lần lưu
    key_version: Mapped[int] = mapped_column(SmallInteger, nullable=False, server_default=text("1"))  # để dành cho đổi khóa chủ
    last4: Mapped[str] = mapped_column(String(4), nullable=False)  # 4 ký tự cuối để admin nhận ra key, hiển thị dạng che
    updated_by: Mapped[str] = mapped_column(String(64), nullable=False)  # SHA-256 id admin, không bao giờ là id thật
    created_at: Mapped[created_at]
    updated_at: Mapped[timestamptz] = mapped_column(server_default=text("now()"), nullable=False)

    __table_args__ = (
        CheckConstraint("updated_by ~ '^[0-9a-f]{64}$'", name="chk_secret_store_updated_by"),
        CheckConstraint("octet_length(nonce) = 12", name="chk_secret_store_nonce"),
    )


__all__ = ["SecretRow"]
