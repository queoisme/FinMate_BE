"""Hợp đồng dữ liệu của AI Financial Agent phía ai-service (khớp ``Evidence`` ở backend: backend dựng bằng chứng, ai-service chỉ nhận và trích dẫn).

Tiền luôn là số nguyên đồng VND: ``StrictInt`` từ chối cả 1_500_000.0 lẫn chuỗi "1500000", để một con số lệch kiểu không lọt qua mà không ai nhận ra.
Không nhận trường lạ (``extra="forbid"``): ai-service không bao giờ được thấy ``user_id`` thật, nên một trường như vậy bị từ chối ngay ở cổng vào."""

from datetime import datetime
from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field, StrictInt

EvidenceSource = Literal["confirmed", "user_entered", "estimated", "document"]


class Evidence(BaseModel):
    model_config = ConfigDict(extra="forbid")

    id: str = Field(min_length=1, max_length=64)
    key: str = Field(min_length=1, max_length=64)
    value_vnd: StrictInt | None = None  # None khi bằng chứng chỉ là chữ (ví dụ một đoạn tài liệu)
    text: str | None = Field(default=None, max_length=500)
    unit: str = Field(min_length=1, max_length=16)
    source: EvidenceSource
    as_of: datetime
    confidence: float = Field(ge=0.0, le=1.0)  # độ đầy đủ × độ mới, KHÔNG phải xác suất thống kê
    source_name: str | None = None
    source_url: str | None = None


class GateRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    text: str = Field(min_length=1, max_length=500)


class GateResponse(BaseModel):
    in_scope: bool
    reason_code: str
    refusal_text: str | None = None  # có khi in_scope = false; lấy từ agent_refusal_templates


class PlanTool(BaseModel):
    """Một công cụ được phép gọi: chỉ tên và mô tả tham số do backend gửi sang, không có mã hay dữ liệu."""

    name: str = Field(min_length=1, max_length=48)
    description: str = ""
    args_schema: dict[str, Any] = Field(default_factory=dict)


class MemoryItem(BaseModel):
    key: str = Field(min_length=1, max_length=48)
    value: str = Field(max_length=200)


class PlanState(BaseModel):
    """Ngữ cảnh ngắn hạn của hội thoại: chỉ các "ô" có cấu trúc của lượt trước (ví dụ ``price_vnd``), không có văn bản thô."""

    last_intent: str | None = None
    slots: dict[str, str] = Field(default_factory=dict)


class PlanFeedback(BaseModel):
    """Công cụ nào bị backend từ chối ở lần chạy trước và vì sao (mã cố định như ``invalid_argument:price_vnd``): bộ lập kế hoạch đọc để sửa tham số."""

    tool: str = Field(min_length=1, max_length=48)
    code: str = Field(min_length=1, max_length=64)


class PlanRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")  # không có user_id: ai-service không bao giờ thấy id thật

    text: str = Field(min_length=1, max_length=500)
    segment: Literal["stable", "variable", "unknown"] = "unknown"
    tools: list[PlanTool] = Field(min_length=1, max_length=16)
    max_steps: int = Field(default=4, ge=1, le=8)
    state: PlanState | None = None
    memory: list[MemoryItem] = Field(default_factory=list, max_length=16)
    feedback: list[PlanFeedback] = Field(default_factory=list, max_length=8)


class PlanStep(BaseModel):
    tool: str
    args: dict[str, Any]


class LlmUsage(BaseModel):
    input_tokens: int
    output_tokens: int
    provider: str
    model: str


class PlanResponse(BaseModel):
    intent: str
    steps: list[PlanStep]
    clarify: str | None = None  # một câu hỏi ngắn khi thiếu thông tin bắt buộc; có thì backend dừng và hỏi lại user
    usage: LlmUsage


class Claim(BaseModel):
    """Một mệnh đề của câu trả lời kèm mã các bằng chứng nó dựa vào. Không có bằng chứng thì chỉ được là câu hỏi lại."""

    text: str = Field(min_length=1, max_length=600)
    evidence_ids: list[str] = Field(default_factory=list, max_length=8)


class Violation(BaseModel):
    claim_index: int
    kind: Literal["number_unsupported", "no_evidence", "unknown_evidence_id"]


class ComposeRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    question: str = Field(min_length=1, max_length=500)
    evidence: list[Evidence] = Field(default_factory=list, max_length=40)
    skip_verifier: bool = False  # chỉ có hiệu lực khi AGENT_EVAL_ENABLED (đánh giá cắt bỏ); ở production bị bỏ qua


class ComposeResponse(BaseModel):
    claims: list[Claim]
    used_template: bool = False  # true = câu dựng từ bằng chứng bằng mẫu cố định (LLM hỏng hoặc hai lần đều không qua kiểm chứng)
    no_data: bool = False  # true = không có bằng chứng nào, câu trả lời chỉ nói "chưa có dữ liệu"
    violations_final: list[Violation] = Field(default_factory=list)
    usage: LlmUsage | None = None  # token của mọi lần gọi LLM trong lượt soạn; None nếu không gọi được lần nào


class MemoryPutRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    user_id_hash: str = Field(pattern=r"^[0-9a-f]{64}$")  # SHA-256 hex; id thật bị từ chối ngay ở cổng vào
    value: str = Field(max_length=200)


class MemoryEntry(BaseModel):
    key: str
    value: str
    updated_at: datetime


class MemoryDeleted(BaseModel):
    deleted: int


__all__ = ["Claim", "ComposeRequest", "MemoryDeleted", "MemoryEntry", "MemoryPutRequest", "ComposeResponse", "Evidence", "EvidenceSource", "GateRequest", "Violation", "GateResponse", "LlmUsage", "MemoryItem", "PlanRequest", "PlanResponse", "PlanState", "PlanStep", "PlanTool"]
