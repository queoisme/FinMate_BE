"""``raw_samples`` — kho thông báo thô để train, đã qua anonymizer.

Chỉ ghi khi ``COLLECT_RAW_SAMPLES`` bật. ``notification_body`` ở đây KHÔNG phải bản gốc:
``app.core.anonymizer.anonymize_body`` đã che số tài khoản/thẻ/điện thoại trước khi lưu.
Bản gốc chỉ tồn tại ở backend (``notification_logs``) và bị xoá sau 90 ngày bởi
``DataCleanupJob`` — phía AI có ``scripts/purge_raw_samples.py`` làm việc tương đương.
"""

from sqlalchemy import Index, String, Text, UniqueConstraint
from sqlalchemy.orm import Mapped, mapped_column

from app.core.enums import SampleSource
from app.db.base import (
    Base,
    created_at,
    enum_check,
    long_text,
    short_text,
    timestamptz,
    uuid_pk,
)


class RawSample(Base):
    __tablename__ = "raw_samples"

    id: Mapped[uuid_pk]
    source: Mapped[str] = mapped_column(String(32), nullable=False)
    package_name: Mapped[short_text]
    notification_title: Mapped[str | None] = mapped_column(Text)
    notification_body: Mapped[long_text]
    received_at: Mapped[timestamptz]

    # Hash chứ không phải user_id thật (AGENTS.md §3.1). Nullable vì mẫu seed không
    # thuộc về người dùng nào.
    user_id_hash: Mapped[str | None] = mapped_column(String(64))

    # SHA-256(package|title|body) — chặn cùng một thông báo vào kho hai lần và làm lệch
    # phân bố tập train.
    content_hash: Mapped[str] = mapped_column(String(64), nullable=False)

    created_at: Mapped[created_at]

    __table_args__ = (
        UniqueConstraint("content_hash", name="uq_raw_samples_content_hash"),
        Index("idx_raw_samples_package_name", "package_name"),
        Index("idx_raw_samples_created_at", "created_at"),
        enum_check("source", SampleSource, "chk_raw_samples_source"),
    )


__all__ = ["RawSample"]
