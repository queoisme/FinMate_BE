"""Thân request của API quản trị định dạng thông báo (``/api/v1/admin/provider-formats``). Phản hồi là dict thuần (xem ``app.services.provider_format_admin``)."""

from typing import Annotated, Any, Literal

from pydantic import BaseModel, Field

from app.db.models.provider_format import CONTRACT_FIELDS, FIELD_DATA_TYPES, FIELD_NORMALIZERS, FIELD_SOURCE_KINDS

ContractField = Literal[CONTRACT_FIELDS]  # type: ignore[valid-type]
DataType = Literal[FIELD_DATA_TYPES]  # type: ignore[valid-type]
SourceKind = Literal[FIELD_SOURCE_KINDS]  # type: ignore[valid-type]
Normalizer = Literal[FIELD_NORMALIZERS]  # type: ignore[valid-type]
Reason = Annotated[str | None, Field(max_length=500)]


class FormatCreate(BaseModel):
    """Định dạng draft TRỐNG cho một nhà cung cấp. Đã có định dạng của package này thì dùng ``clone`` để giữ pattern, trường và mẫu."""

    provider_key: Annotated[str, Field(pattern=r"^[a-z0-9_]{1,100}$")]
    package_name: Annotated[str, Field(min_length=1, max_length=120)]
    priority: Annotated[int, Field(ge=0, le=100000)] = 100
    notes: Annotated[str | None, Field(max_length=1000)] = None
    reason: Reason = None


class FormatClone(BaseModel):
    package_name: Annotated[str, Field(min_length=1, max_length=120)]
    reason: Reason = None


class FormatUpdate(BaseModel):
    priority: Annotated[int | None, Field(ge=0, le=100000)] = None
    notes: Annotated[str | None, Field(max_length=1000)] = None


class Reasoned(BaseModel):
    reason: Reason = None


class PatternCreate(BaseModel):
    pattern_name: Annotated[str, Field(min_length=1, max_length=120)]
    regex: Annotated[str, Field(min_length=1, max_length=4000)]
    transaction_type: Literal["debit", "credit"] | None = None  # None = suy từ named group ``sign``
    priority: Annotated[int, Field(ge=0, le=100000)] = 100
    sample_text: Annotated[str | None, Field(max_length=2000)] = None
    is_active: bool = True
    notes: Annotated[str | None, Field(max_length=1000)] = None


class PatternUpdate(BaseModel):
    """Chỉ các khóa có mặt trong thân request mới đổi (``transaction_type: null`` là đặt về "suy từ sign")."""

    pattern_name: Annotated[str | None, Field(min_length=1, max_length=120)] = None
    regex: Annotated[str | None, Field(min_length=1, max_length=4000)] = None
    transaction_type: Literal["debit", "credit"] | None = None
    priority: Annotated[int | None, Field(ge=0, le=100000)] = None
    sample_text: Annotated[str | None, Field(max_length=2000)] = None
    is_active: bool | None = None
    notes: Annotated[str | None, Field(max_length=1000)] = None


class FieldCreate(BaseModel):
    field_key: Annotated[str, Field(pattern=r"^[a-z][a-z0-9_]{0,63}$")]
    maps_to: ContractField | None = None  # None = trường phụ (vào ops_analyze_log.extra_fields)
    data_type: DataType = "text"
    is_required: bool = False
    source_kind: SourceKind = "regex_group"
    source_ref: Annotated[str, Field(min_length=1, max_length=500)]  # tên named group, hoặc giá trị cố định khi source_kind = literal
    normalizer: Normalizer | None = None
    is_sensitive: bool = False  # chỉ hợp lệ với trường hợp đồng (maps_to có giá trị): trường phụ không được nhạy cảm


class FieldUpdate(BaseModel):
    """Chỉ các khóa có mặt trong thân request mới đổi (``maps_to: null`` là chuyển thành trường phụ)."""

    maps_to: ContractField | None = None
    data_type: DataType | None = None
    is_required: bool | None = None
    source_kind: SourceKind | None = None
    source_ref: Annotated[str | None, Field(min_length=1, max_length=500)] = None
    normalizer: Normalizer | None = None
    is_sensitive: bool | None = None


class SampleCreate(BaseModel):
    sample_text: Annotated[str, Field(min_length=1, max_length=2000)]  # PHẢI là mẫu đã che dữ liệu cá nhân (người nhập chịu trách nhiệm)
    expected_fields: dict[str, Any]


class TryBody(BaseModel):
    text: Annotated[str, Field(min_length=1, max_length=2000)]  # chạy thử các pattern đang bật của định dạng này; không ghi gì, không log nội dung
