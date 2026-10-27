"""``kb_provider_formats`` + ``kb_provider_format_fields`` + ``kb_provider_format_samples`` — định dạng thông báo của một nhà cung cấp, có PHIÊN BẢN.

Một định dạng gom các regex (``kb_provider_patterns``) của một ``package_name``, khai báo cách mỗi named group thành trường (``kb_provider_format_fields``) và mang các
mẫu kiểm tra (``kb_provider_format_samples``). Sửa regex = nhân bản định dạng thành bản ``draft`` mới, sửa, rồi kích hoạt: bản ``active`` đang chạy không bị đụng tới.

Trạng thái: ``draft`` (đang soạn, matcher bỏ qua) -> ``active`` (matcher dùng; nhiều bản cùng active được, thử theo ``priority``) -> ``archived``.
CỔNG KÍCH HOẠT: ``draft`` chỉ thành ``active`` khi MỌI mẫu khớp ``expected_fields`` (``app.services.provider_formats.activate``). Cổng nằm ở tầng ứng dụng, không phải ràng buộc DB:
UPDATE status thẳng bằng SQL vượt được nó, nên mọi thay đổi trạng thái đi qua service hoặc ``scripts/provider_format.py``.

``is_sensitive``: trường nhạy cảm (số tài khoản, thẻ, số điện thoại) KHÔNG bao giờ được ghi vào ``extra_fields`` hay log. CHECK ``NOT (is_sensitive AND maps_to IS NULL)`` (F1-04, migration 0027)
bảo đảm điều đó ở DB: trường nhạy cảm phải ánh xạ vào trường hợp đồng; muốn bóc một số tài khoản mà không lưu thì không khai báo trường đó.
Quản trị qua HTTP: ``app/api/v1/provider_formats_admin.py`` (``/api/v1/admin/provider-formats``); công cụ dòng lệnh ``scripts/provider_format.py`` vẫn dùng được.
"""

from sqlalchemy import CheckConstraint, ForeignKey, Index, Integer, String, Text, UniqueConstraint, text
from sqlalchemy.dialects.postgresql import JSONB, UUID
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, created_at, short_text, uuid_pk

FORMAT_STATUSES = ("draft", "active", "archived")
# Sáu trường của hợp đồng với backend; maps_to rỗng = trường phụ (vào extra_fields).
CONTRACT_FIELDS = ("amount_cents", "transaction_type", "merchant_name", "description", "transacted_at", "balance_after_cents")
FIELD_DATA_TYPES = ("text", "amount", "datetime")
FIELD_SOURCE_KINDS = ("regex_group", "literal")  # không có "derived": giá trị suy ra bằng code là việc của engine, không khai báo được ở DB
FIELD_NORMALIZERS = ("vnd_amount", "clean_text")  # tên hàm trong danh sách trắng của code; quản trị viên chọn, không viết


def _in(column: str, values: tuple[str, ...]) -> str:
    return f"{column} IN ({', '.join(repr(v) for v in values)})"


class ProviderFormat(Base):
    __tablename__ = "kb_provider_formats"

    id: Mapped[uuid_pk]
    provider_key: Mapped[short_text]
    package_name: Mapped[short_text]
    version: Mapped[int] = mapped_column(Integer, nullable=False)
    status: Mapped[str] = mapped_column(String(10), nullable=False)
    priority: Mapped[int] = mapped_column(Integer, nullable=False, server_default=text("100"))  # nhỏ hơn được thử trước (giữa các bản cùng active)
    notes: Mapped[str | None] = mapped_column(Text)
    created_by: Mapped[str] = mapped_column(String(64), nullable=False)  # SHA-256 id admin hoặc của tiến trình hệ thống, không phải id thật
    created_at: Mapped[created_at]
    updated_at: Mapped[created_at]

    __table_args__ = (
        UniqueConstraint("package_name", "version", name="uq_kb_provider_formats_package_version"),
        # Truy vấn của bộ khớp: định dạng đang bật của một package, theo thứ tự ưu tiên.
        Index("idx_kb_provider_formats_match", "package_name", "status", "priority"),
        CheckConstraint(_in("status", FORMAT_STATUSES), name="chk_kb_provider_formats_status"),
        CheckConstraint("version >= 1", name="chk_kb_provider_formats_version"),
        CheckConstraint("priority >= 0", name="chk_kb_provider_formats_priority"),
        CheckConstraint("created_by ~ '^[0-9a-f]{64}$'", name="chk_kb_provider_formats_created_by"),
    )


class ProviderFormatField(Base):
    __tablename__ = "kb_provider_format_fields"

    id: Mapped[uuid_pk]
    format_id: Mapped[UUID] = mapped_column(UUID(as_uuid=True), ForeignKey("kb_provider_formats.id", ondelete="CASCADE"), nullable=False)
    field_key: Mapped[str] = mapped_column(String(64), nullable=False)
    maps_to: Mapped[str | None] = mapped_column(String(32))  # một trong CONTRACT_FIELDS, hoặc NULL = trường phụ
    data_type: Mapped[str] = mapped_column(String(16), nullable=False)
    is_required: Mapped[bool] = mapped_column(nullable=False, server_default=text("false"))
    source_kind: Mapped[str] = mapped_column(String(16), nullable=False)
    source_ref: Mapped[str] = mapped_column(Text, nullable=False)  # tên named group (regex_group) hoặc giá trị cố định (literal)
    normalizer: Mapped[str | None] = mapped_column(String(32))
    normalizer_arg: Mapped[str | None] = mapped_column(Text)  # tham số cho bộ chuẩn hóa; chưa bộ nào trong FIELD_NORMALIZERS dùng
    is_sensitive: Mapped[bool] = mapped_column(nullable=False, server_default=text("false"))
    created_at: Mapped[created_at]

    __table_args__ = (
        UniqueConstraint("format_id", "field_key", name="uq_kb_provider_format_fields_key"),
        # Hai trường không cùng ánh xạ vào một trường hợp đồng: nếu không, "amount" lấy từ group nào là tuỳ may.
        Index("uq_kb_provider_format_fields_maps_to", "format_id", "maps_to", unique=True, postgresql_where=text("maps_to IS NOT NULL")),
        CheckConstraint(f"maps_to IS NULL OR {_in('maps_to', CONTRACT_FIELDS)}", name="chk_kb_provider_format_fields_maps_to"),
        CheckConstraint(_in("data_type", FIELD_DATA_TYPES), name="chk_kb_provider_format_fields_data_type"),
        CheckConstraint(_in("source_kind", FIELD_SOURCE_KINDS), name="chk_kb_provider_format_fields_source_kind"),
        CheckConstraint(f"normalizer IS NULL OR {_in('normalizer', FIELD_NORMALIZERS)}", name="chk_kb_provider_format_fields_normalizer"),
        # F1-04: trường nhạy cảm không được là trường phụ (trường phụ đi vào ops_analyze_log.extra_fields). Muốn bóc mà không lưu thì không khai báo trường đó.
        CheckConstraint("NOT (is_sensitive AND maps_to IS NULL)", name="chk_kb_provider_format_fields_sensitive_mapped"),
    )


class ProviderFormatSample(Base):
    __tablename__ = "kb_provider_format_samples"

    id: Mapped[uuid_pk]
    format_id: Mapped[UUID] = mapped_column(UUID(as_uuid=True), ForeignKey("kb_provider_formats.id", ondelete="CASCADE"), nullable=False)
    sample_text: Mapped[str] = mapped_column(Text, nullable=False)  # PHẢI là mẫu đã che dữ liệu cá nhân (quy tắc của người nhập; DB không kiểm được)
    # Các trường cần so khi kiểm: amount_cents, transaction_type, merchant_name, description, balance_after_cents, transacted_at (chữ gốc) và khóa của trường phụ.
    # Chỉ so các khóa có mặt, nên một mẫu có thể chỉ khẳng định số tiền.
    expected_fields: Mapped[dict] = mapped_column(JSONB, nullable=False)
    created_at: Mapped[created_at]

    __table_args__ = (
        Index("idx_kb_provider_format_samples_format", "format_id"),
        CheckConstraint("jsonb_typeof(expected_fields) = 'object'", name="chk_kb_provider_format_samples_expected"),
        CheckConstraint("char_length(sample_text) <= 2000", name="chk_kb_provider_format_samples_text_len"),
    )


__all__ = [
    "CONTRACT_FIELDS",
    "FIELD_DATA_TYPES",
    "FIELD_NORMALIZERS",
    "FIELD_SOURCE_KINDS",
    "FORMAT_STATUSES",
    "ProviderFormat",
    "ProviderFormatField",
    "ProviderFormatSample",
]
