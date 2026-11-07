"""Kho tri thức của AI Financial Agent (RAG): tài liệu do người duyệt (Draft → Approved → Retired) và các đoạn của chúng kèm vector.

Chỉ tài liệu ``Approved`` và còn hiệu lực (``effective_from <= now < effective_to``) mới được truy xuất; mọi tài liệu phải có ``source_name`` và ``as_of`` để câu trả lời ghi được nguồn và ngày. Số chiều vector cố định 768
(``settings.agent_embedding_dim``): đổi số chiều phải làm migration mới và nhúng lại toàn bộ."""

import uuid

from sqlalchemy import CheckConstraint, Computed, ForeignKey, Index, Integer, String, Text, UniqueConstraint
from sqlalchemy import text as sql_text
from sqlalchemy.dialects.postgresql import TSVECTOR, UUID
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, created_at, timestamptz, uuid_pk
from app.db.vector import Vector

EMBEDDING_DIM = 768
KB_STATUSES = ("Draft", "Approved", "Retired")


class AgentKbDocument(Base):
    __tablename__ = "agent_kb_documents"

    id: Mapped[uuid_pk]
    title: Mapped[str] = mapped_column(String(200), nullable=False)
    source_name: Mapped[str] = mapped_column(String(200), nullable=False)
    source_url: Mapped[str | None] = mapped_column(String(500))
    as_of: Mapped[timestamptz] = mapped_column(nullable=False)  # ngày nội dung được cập nhật / công bố
    effective_from: Mapped[timestamptz] = mapped_column(nullable=False)
    effective_to: Mapped[timestamptz | None]
    status: Mapped[str] = mapped_column(String(10), nullable=False, server_default=sql_text("'Draft'"))
    version: Mapped[int] = mapped_column(Integer, nullable=False, server_default=sql_text("1"))
    created_at: Mapped[created_at]
    updated_at: Mapped[timestamptz] = mapped_column(server_default=sql_text("now()"), nullable=False)

    __table_args__ = (
        CheckConstraint("status IN (" + ", ".join(f"'{s}'" for s in KB_STATUSES) + ")", name="chk_agent_kb_documents_status"),
        CheckConstraint("effective_to IS NULL OR effective_to > effective_from", name="chk_agent_kb_documents_window"),
        Index("ix_agent_kb_documents_status", "status"),
    )


class AgentKbChunk(Base):
    __tablename__ = "agent_kb_chunks"

    id: Mapped[uuid_pk]
    document_id: Mapped[uuid.UUID] = mapped_column(UUID(as_uuid=True), ForeignKey("agent_kb_documents.id", ondelete="CASCADE"), nullable=False)
    chunk_index: Mapped[int] = mapped_column(Integer, nullable=False)
    text: Mapped[str] = mapped_column(Text, nullable=False)
    tsv = mapped_column(TSVECTOR, Computed("to_tsvector('simple', text)", persisted=True), nullable=False)  # 'simple': khớp theo chữ đúng dấu, không cần extension unaccent
    embedding = mapped_column(Vector(EMBEDDING_DIM), nullable=False)
    created_at: Mapped[created_at]

    __table_args__ = (
        UniqueConstraint("document_id", "chunk_index", name="uq_agent_kb_chunks_doc_index"),
        Index("ix_agent_kb_chunks_tsv", "tsv", postgresql_using="gin"),
        # Không có chỉ mục ANN (hnsw/ivfflat): kho vài nghìn đoạn thì quét đúng (exact) vừa nhanh vừa không mất độ chính xác; thêm hnsw khi vượt ~100 nghìn đoạn.
    )
