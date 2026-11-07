"""Bộ kiểm chứng số của AI Financial Agent: câu trả lời do LLM viết chỉ được giữ nếu MỌI số tiền trong nó bằng giá trị của bằng chứng mà mệnh đề đó trích dẫn.

Thuần Python, KHÔNG gọi LLM (kiểm bằng chính thứ cần kiểm thì vô nghĩa). Số tiền được đọc bằng bộ đọc số tất định của dự án (``find_amounts``: "1,5 triệu", "600tr", "75k", "1.500.000 đồng") cộng thêm số trơn dài
("1500000") để một con số không đơn vị cũng không lọt. Khớp là so bằng ĐÚNG số nguyên đồng — làm tròn kiểu "xấp xỉ 1,5 triệu" cho 1.520.000 là không khớp, vì tiền không được phép là một con số gần đúng do mô hình tự chọn.
Chỉ SỐ TIỀN bị kiểm: số tháng, phần trăm, năm không phải tiền."""

from __future__ import annotations

import re

from app.agent.schemas import Claim, Evidence, Violation
from app.services.amount_normalizer import find_amounts

MIN_AMOUNT_VND = 1_000  # nhỏ hơn không phải một khoản tiền ("2" trong "2 năm")
_YEAR = re.compile(r"(?i)\bnăm\s+(?:19|20)\d{2}\b")
_DATE = re.compile(r"\b\d{1,2}/\d{1,2}/\d{4}\b")
_PLAIN = re.compile(r"(?<![\d.,])(\d{1,3}(?:[.,]\d{3})+|\d{4,})(?![\d]|[.,]\d)")  # 1.500.000 hoặc 1500000


def format_vnd(amount: int) -> str:
    """1500000 → "1.500.000 đồng" (dấu chấm ngăn nghìn theo cách viết của người Việt)."""
    return f"{amount:,}".replace(",", ".") + " đồng"


def claim_amounts(text: str) -> set[int]:
    """Mọi số tiền (đồng) xuất hiện trong ``text``."""
    cleaned = _DATE.sub(" ", _YEAR.sub(" ", text))
    amounts = {match.value for match in find_amounts(cleaned) if match.value >= MIN_AMOUNT_VND}
    for match in _PLAIN.finditer(cleaned):
        token = match.group(1)
        value = int(re.sub(r"[.,]", "", token))
        if value >= MIN_AMOUNT_VND:
            amounts.add(value)
    return amounts


def verify(claims: list[Claim], evidence: list[Evidence]) -> list[Violation]:
    by_id = {e.id: e for e in evidence}
    violations: list[Violation] = []
    for index, claim in enumerate(claims):
        if not claim.evidence_ids:
            # không có bằng chứng thì chỉ được là câu hỏi lại, và câu hỏi đó cũng không được kèm một số tiền chưa ai kiểm
            if not (claim.text.strip().endswith("?") and not claim_amounts(claim.text)):
                violations.append(Violation(claim_index=index, kind="no_evidence"))
            continue
        if any(evidence_id not in by_id for evidence_id in claim.evidence_ids):
            violations.append(Violation(claim_index=index, kind="unknown_evidence_id"))
            continue
        cited = {abs(by_id[i].value_vnd) for i in claim.evidence_ids if by_id[i].value_vnd is not None}  # "thiếu 500.000 đồng" trích bằng chứng −500.000
        if any(amount not in cited for amount in claim_amounts(claim.text)):
            violations.append(Violation(claim_index=index, kind="number_unsupported"))
    return violations
