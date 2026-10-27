"""``config_documents`` — tài liệu cấu hình JSON có phiên bản (từ vựng hiểu câu chat, từ vựng và ngưỡng đọc hóa đơn), sửa được từ trang quản trị.

Mỗi lần lưu là MỘT DÒNG MỚI (``version`` tăng dần), không bao giờ sửa dòng cũ: lịch sử là các dòng đó, hoàn tác là kích hoạt lại một phiên bản cũ.
Partial unique index ``uq_config_documents_active`` bảo đảm mỗi tài liệu có ĐÚNG MỘT bản đang chạy ở tầng DB (cùng cách ``model_versions``), không phải quy ước trong code.
Không có dòng ``active`` thì dịch vụ dùng bản mặc định nằm trong code/image."""

from sqlalchemy import CheckConstraint, Index, Integer, String, Text, UniqueConstraint, text
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, created_at, timestamptz, uuid_pk


class ConfigDocument(Base):
    __tablename__ = "config_documents"

    id: Mapped[uuid_pk]
    name: Mapped[str] = mapped_column(String(48), nullable=False)  # intent_cues | chat_dictionary | merchant_lexicon | parsing_vocab | thresholds | app_screens
    version: Mapped[int] = mapped_column(Integer, nullable=False)
    status: Mapped[str] = mapped_column(String(10), nullable=False)  # draft | active | archived
    content: Mapped[dict] = mapped_column(JSONB, nullable=False)
    note: Mapped[str | None] = mapped_column(Text)  # lý do đổi (bắt buộc nhập ở API: ai đọc lịch sử cũng hiểu vì sao)
    created_by: Mapped[str] = mapped_column(String(64), nullable=False)  # SHA-256 id admin, không bao giờ là id thật
    created_at: Mapped[created_at]
    activated_at: Mapped[timestamptz | None] = mapped_column()

    __table_args__ = (
        UniqueConstraint("name", "version", name="uq_config_documents_name_version"),
        Index("uq_config_documents_active", "name", unique=True, postgresql_where=text("status = 'active'")),
        CheckConstraint("status IN ('draft', 'active', 'archived')", name="chk_config_documents_status"),
        CheckConstraint("version >= 1", name="chk_config_documents_version"),
        CheckConstraint("jsonb_typeof(content) = 'object'", name="chk_config_documents_content_object"),
        CheckConstraint("created_by ~ '^[0-9a-f]{64}$'", name="chk_config_documents_created_by"),
    )


__all__ = ["ConfigDocument"]
