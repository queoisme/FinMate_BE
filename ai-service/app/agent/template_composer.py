"""Câu trả lời dựng THẲNG từ bằng chứng bằng mẫu cố định, không qua LLM. Dùng khi LLM hỏng hoặc hai lần soạn đều không qua bộ kiểm chứng.

Xấu hơn câu LLM viết (khô, liệt kê) nhưng LUÔN đúng: mỗi mệnh đề là một bằng chứng kèm nguồn và ngày cập nhật, số tiền in đúng giá trị của chính bằng chứng đó, nên luôn qua ``verify``."""

from __future__ import annotations

from datetime import timedelta, timezone

from app.agent.schemas import Claim, Evidence
from app.agent.verifier import format_vnd

MAX_CLAIMS = 12
_VN = timezone(timedelta(hours=7))
_SOURCE_LABEL = {"confirmed": "đã xác nhận", "user_entered": "bạn nhập", "estimated": "ước tính", "document": "theo tài liệu"}


def compose_from_template(evidence: list[Evidence]) -> list[Claim]:
    claims: list[Claim] = []
    for item in evidence:
        if len(claims) >= MAX_CLAIMS:
            break
        if item.value_vnd is not None:
            body = f"{item.text or item.key}: {format_vnd(item.value_vnd)}"
        elif item.text:
            body = item.text
        else:
            continue
        stamp = f"{_SOURCE_LABEL[item.source]}, cập nhật {item.as_of.astimezone(_VN).strftime('%d/%m/%Y')}"
        claims.append(Claim(text=f"{body} ({stamp}).", evidence_ids=[item.id]))
    return claims
