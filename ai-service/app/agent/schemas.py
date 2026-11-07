"""Hợp đồng dữ liệu của AI Financial Agent phía ai-service (khớp ``Evidence`` ở backend: backend dựng bằng chứng, ai-service chỉ nhận và trích dẫn).

Tiền luôn là số nguyên đồng VND: ``StrictInt`` từ chối cả 1_500_000.0 lẫn chuỗi "1500000", để một con số lệch kiểu không lọt qua mà không ai nhận ra.
Không nhận trường lạ (``extra="forbid"``): ai-service không bao giờ được thấy ``user_id`` thật, nên một trường như vậy bị từ chối ngay ở cổng vào."""

from datetime import datetime
from typing import Literal

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


__all__ = ["Evidence", "EvidenceSource"]
