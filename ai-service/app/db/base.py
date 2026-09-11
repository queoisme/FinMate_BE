"""Declarative base + kiểu cột dùng chung cho AI DB.

Quy ước lấy từ ARCHITECTURE.md §4.3: id là UUID sinh bởi ``gen_random_uuid()``, tiền là
BIGINT đơn vị đồng VND, thời gian là TIMESTAMPTZ, enum là TEXT + CHECK constraint.
"""

import uuid
from datetime import datetime
from typing import Annotated, Any, ClassVar

from sqlalchemy import BigInteger, DateTime, String, Text, text
from sqlalchemy.dialects.postgresql import JSONB, UUID
from sqlalchemy.orm import DeclarativeBase, mapped_column

uuid_pk = Annotated[
    uuid.UUID,
    mapped_column(
        UUID(as_uuid=True),
        primary_key=True,
        server_default=text("gen_random_uuid()"),
    ),
]

uuid_fk = Annotated[uuid.UUID, mapped_column(UUID(as_uuid=True))]

# TIMESTAMPTZ do DB tự điền — tránh lệch đồng hồ giữa các tiến trình ghi.
created_at = Annotated[
    datetime,
    mapped_column(
        DateTime(timezone=True), nullable=False, server_default=text("now()")
    ),
]

timestamptz = Annotated[datetime, mapped_column(DateTime(timezone=True))]

# Đồng VND. BIGINT, không bao giờ NUMERIC/FLOAT (AGENTS.md §3.2).
money = Annotated[int, mapped_column(BigInteger)]

# SHA-256 hex — 64 ký tự. AI DB không bao giờ giữ id thật của backend (AGENTS.md §3.1).
sha256_hex = Annotated[str, mapped_column(String(64))]

short_text = Annotated[str, mapped_column(String(120))]
long_text = Annotated[str, mapped_column(Text)]
jsonb = Annotated[dict[str, Any], mapped_column(JSONB)]


class Base(DeclarativeBase):
    # ClassVar để ruff không coi đây là cột mutable — nó là API của SQLAlchemy.
    type_annotation_map: ClassVar[dict] = {
        dict[str, Any]: JSONB,
    }


def enum_check(column: str, enum_cls: Any, name: str, nullable: bool = False) -> Any:
    """CHECK constraint sinh từ enum Python — không gõ tay danh sách giá trị hai lần.

    ARCHITECTURE.md §4.3 yêu cầu enum lưu dạng TEXT + CHECK thay vì PostgreSQL ENUM type.
    """
    from sqlalchemy import CheckConstraint

    values = ", ".join(f"'{value}'" for value in enum_cls.values())
    condition = f"{column} IN ({values})"
    if nullable:
        condition = f"{column} IS NULL OR {condition}"
    return CheckConstraint(condition, name=name)
