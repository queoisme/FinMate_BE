"""Request schema — khớp đúng ARCHITECTURE.md §3.3 và
``backend/FinMate.Infrastructure/ExternalServices/AIServiceApiContracts.cs``.

Tên field ở đây là snake_case và PHẢI giữ nguyên: phía C# gắn ``[JsonPropertyName]`` theo
đúng các chuỗi này. Đổi một tên là phá contract, mà lỗi sẽ hiện ra dưới dạng field null
lặng lẽ chứ không phải exception.
"""

from __future__ import annotations

import uuid
from datetime import datetime

from pydantic import BaseModel, ConfigDict, Field


class AnalyzeRequest(BaseModel):
    model_config = ConfigDict(extra="ignore")

    backend_request_id: uuid.UUID
    user_id_hash: str = Field(min_length=64, max_length=64)
    package_name: str = Field(min_length=1, max_length=120)
    notification_title: str | None = None
    notification_body: str = Field(min_length=1)
    received_at: datetime


class FeedbackRequest(BaseModel):
    model_config = ConfigDict(extra="ignore")

    backend_transaction_id_hash: str = Field(min_length=64, max_length=64)
    user_id_hash: str = Field(min_length=64, max_length=64)

    # Contract gọi là pipeline_request_id nhưng backend gửi id notification_log của nó,
    # tức là khớp với pipeline_requests.backend_request_id. Hiện backend gửi null —
    # xem ghi chú "liên kết feedback" ở app/api/v1/feedback.py.
    pipeline_request_id: uuid.UUID | None = None

    package_name: str = Field(min_length=1, max_length=120)
    predicted_category: str | None = None
    corrected_category: str | None = None
    feedback_type: str = Field(min_length=1, max_length=32)
