"""Câu trả lời dựng THẲNG từ bằng chứng bằng mẫu cố định, không qua LLM. Dùng khi LLM hỏng hoặc hai lần soạn đều không qua bộ kiểm chứng.

Xấu hơn câu LLM viết (khô, liệt kê) nhưng LUÔN đúng: mỗi mệnh đề là một bằng chứng kèm nguồn và ngày cập nhật, số tiền in đúng giá trị của chính bằng chứng đó, nên luôn qua ``verify``."""

from __future__ import annotations

import re
from datetime import timedelta, timezone

from app.agent.schemas import Claim, Evidence
from app.agent.verifier import format_vnd

MAX_CLAIMS = 12
_VN = timezone(timedelta(hours=7))
_SOURCE_LABEL = {"confirmed": "đã xác nhận", "user_entered": "bạn nhập", "estimated": "ước tính", "document": "theo tài liệu"}


_VERDICT_PREFIX = "verdict: "
_VERDICT_LABEL = {"feasible": "Khả thi", "tight": "Cần cân nhắc", "infeasible": "Khó đạt"}
_SEGMENT = re.compile(r",\s*nhóm thu nhập \w+")


def verdict_display(text: str) -> str:
    """``verdict: infeasible (horizon 24 tháng, nhóm thu nhập stable)`` là chuỗi máy đọc, không đưa nguyên cho người dùng."""
    if not text.startswith(_VERDICT_PREFIX):
        return text
    word, _, detail = text[len(_VERDICT_PREFIX):].partition(" ")
    label = _VERDICT_LABEL.get(word, word)
    detail = _SEGMENT.sub("", detail.strip()).replace("horizon ", "mốc ")
    return f"Kết luận: {label} {detail}".rstrip()


def compose_from_template(evidence: list[Evidence]) -> list[Claim]:
    claims: list[Claim] = []
    for item in evidence:
        if len(claims) >= MAX_CLAIMS:
            break
        if item.value_vnd is not None:
            body = f"{item.text or item.key}: {format_vnd(item.value_vnd)}"
        elif item.text:
            body = verdict_display(item.text) if item.id == "verdict" else item.text
        else:
            continue
        stamp = f"{_SOURCE_LABEL[item.source]}, cập nhật {item.as_of.astimezone(_VN).strftime('%d/%m/%Y')}"
        claims.append(Claim(text=f"{body} ({stamp}).", evidence_ids=[item.id]))
    return claims
