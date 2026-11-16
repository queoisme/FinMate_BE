"""Bảng nền của AI Financial Agent: vết chạy, câu từ chối mẫu, bộ nhớ dài hạn của người dùng.

AI DB không bao giờ giữ id thật của người dùng (AGENTS.md §3.1): mọi cột ``user_id_hash`` bị CHECK ép đúng dạng SHA-256 hex. ``agent_runs`` chỉ ghi tên bước,
tên công cụ, độ trễ, số token và kết luận kiểm chứng — KHÔNG có cột nào chứa câu hỏi hay số tiền. ``agent_user_memory`` chỉ là sở thích ngắn (≤200 ký tự) do chính
người dùng xem/sửa/xóa được; khóa hợp lệ do code quy định (``AgentLimits.MemoryKeys`` ở backend), DB chỉ giữ giới hạn độ dài và dạng hash."""

import uuid

from sqlalchemy import Boolean, CheckConstraint, Index, Integer, String, Text, UniqueConstraint
from sqlalchemy import text as sql_text  # cột AgentRefusalTemplate.text che mất tên `text` trong thân lớp
from sqlalchemy.dialects.postgresql import UUID
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, created_at, timestamptz, uuid_pk

HASH_CHECK = "~ '^[0-9a-f]{64}$'"
AGENT_STEPS = ("context", "gate", "plan", "execute", "compose", "verify", "respond")


class AgentRun(Base):
    __tablename__ = "agent_runs"

    id: Mapped[uuid_pk]
    trace_id: Mapped[uuid.UUID] = mapped_column(UUID(as_uuid=True), nullable=False)  # một lượt hỏi = một trace_id, backend tạo và truyền sang qua X-Trace-Id
    user_id_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    step: Mapped[str] = mapped_column(String(16), nullable=False)
    tool: Mapped[str | None] = mapped_column(String(48))  # chỉ có ở bước execute
    latency_ms: Mapped[int] = mapped_column(Integer, nullable=False)
    input_tokens: Mapped[int] = mapped_column(Integer, nullable=False, server_default=sql_text("0"))
    output_tokens: Mapped[int] = mapped_column(Integer, nullable=False, server_default=sql_text("0"))
    verdict: Mapped[str | None] = mapped_column(String(24))  # kết luận bộ kiểm chứng / cổng phạm vi; không bao giờ là nội dung câu trả lời
    created_at: Mapped[created_at]

    __table_args__ = (
        Index("ix_agent_runs_trace", "trace_id"),
        Index("ix_agent_runs_user_created", "user_id_hash", "created_at"),
        CheckConstraint(f"user_id_hash {HASH_CHECK}", name="chk_agent_runs_user_hash"),
        CheckConstraint("step IN (" + ", ".join(f"'{s}'" for s in AGENT_STEPS) + ")", name="chk_agent_runs_step"),
        CheckConstraint("latency_ms >= 0 AND input_tokens >= 0 AND output_tokens >= 0", name="chk_agent_runs_non_negative"),
    )


class AgentRefusalTemplate(Base):
    """Câu từ chối mẫu (ngoài phạm vi, đòi tư vấn đầu tư...): sửa qua DB, không hard-code trong code."""

    __tablename__ = "agent_refusal_templates"

    code: Mapped[str] = mapped_column(String(32), primary_key=True)
    text: Mapped[str] = mapped_column(Text, nullable=False)
    is_active: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default=sql_text("true"))
    created_at: Mapped[created_at]
    updated_at: Mapped[timestamptz] = mapped_column(server_default=sql_text("now()"), nullable=False)


class AgentUserMemory(Base):
    __tablename__ = "agent_user_memory"

    user_id_hash: Mapped[str] = mapped_column(String(64), primary_key=True)
    key: Mapped[str] = mapped_column(String(48), primary_key=True)
    value: Mapped[str] = mapped_column(String(200), nullable=False)
    updated_at: Mapped[timestamptz] = mapped_column(server_default=sql_text("now()"), nullable=False)

    __table_args__ = (CheckConstraint(f"user_id_hash {HASH_CHECK}", name="chk_agent_user_memory_user_hash"),)


class AgentScopeCue(Base):
    """Cụm từ của cổng phạm vi (``app/agent/gate.py``): ``forbidden`` (theo chủ đề: prompt_injection, other_users_data, investment_advice) hoặc ``in_scope`` (chủ đề ``finance``).

    Lưu ĐÃ BỎ DẤU, chữ thường (cổng so khớp trên câu đã bỏ dấu). Admin thêm/tắt cụm qua DB, không cần đổi code; bản mặc định chỉ được gieo thêm, không bao giờ ghi đè."""

    __tablename__ = "agent_scope_cues"

    id: Mapped[uuid_pk]
    kind: Mapped[str] = mapped_column(String(12), nullable=False)
    topic: Mapped[str] = mapped_column(String(32), nullable=False)
    phrase: Mapped[str] = mapped_column(String(80), nullable=False)
    is_active: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default=sql_text("true"))
    created_at: Mapped[created_at]

    __table_args__ = (
        UniqueConstraint("kind", "topic", "phrase", name="uq_agent_scope_cues_kind_topic_phrase"),
        CheckConstraint("kind IN ('forbidden', 'in_scope')", name="chk_agent_scope_cues_kind"),
        CheckConstraint("length(phrase) > 0 AND phrase = lower(btrim(phrase))", name="chk_agent_scope_cues_phrase"),
    )


__all__ = ["AGENT_STEPS", "AgentRefusalTemplate", "AgentRun", "AgentScopeCue", "AgentUserMemory"]
