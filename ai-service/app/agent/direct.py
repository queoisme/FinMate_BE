"""Đường tắt của AI Financial Agent: câu hỏi quen thuộc về DỮ LIỆU QUẢN LÝ ("ngân sách còn bao nhiêu", "mục tiêu đến đâu rồi", "sắp đến hạn khoản nào", "tháng này tiêu nhiều nhất vào đâu")
chỉ có MỘT công cụ đúng, nên luật chọn luôn công cụ và backend bỏ lượt LLM lập kế hoạch: rẻ hơn (lượt đó chiếm khoảng một nửa token), nhanh hơn, và không phụ thuộc LLM ở bước đầu.

Chỉ là ĐƯỜNG TẮT: câu không khớp rõ ràng (không nói kỳ, có số tiền, hoặc khớp hai công cụ cùng lúc) trả None và vẫn qua LLM lập kế hoạch như cũ. Backend vẫn kiểm công cụ và tham số như mọi kế hoạch.
Mẫu nhận dạng nằm ở app/data/intent_cues.json (nhóm ``agent_direct_*``), không có từ khóa nào trong code."""

from __future__ import annotations

from dataclasses import dataclass

from app.services import intent_cues
from app.services.amount_normalizer import find_amounts
from app.services.vi_text import clean_text, fold

MIN_AMOUNT_VND = 1_000
MIN_MONTHS, MAX_MONTHS = 1, 12  # khoảng months của công cụ spending_summary (hợp đồng với backend)
_NO_ARG_TOOLS = (("budget_status", "agent_direct_budget"), ("goals_progress", "agent_direct_goals"), ("upcoming_items", "agent_direct_upcoming"))


@dataclass(frozen=True)
class DirectRoute:
    tool: str
    args: dict[str, int]


def direct_route(text: str) -> DirectRoute | None:
    """Công cụ và tham số cho câu hỏi quen, hoặc None để LLM lập kế hoạch."""
    folded = " ".join(fold(clean_text(text)).split())
    if any(match.value >= MIN_AMOUNT_VND for match in find_amounts(text)):
        return None  # câu có số tiền là lệnh hoặc câu kế hoạch ("đặt hạn mức...", "nếu tôi tiết kiệm 5 triệu"): việc của LLM lập kế hoạch
    patterns = intent_cues.current().patterns
    found = [DirectRoute(tool, {}) for tool, name in _NO_ARG_TOOLS if patterns[name].search(folded)]
    if patterns["agent_direct_spending"].search(folded):
        months = patterns["agent_direct_months"].search(folded)
        if months and MIN_MONTHS <= int(months.group("n")) <= MAX_MONTHS:
            found.append(DirectRoute("spending_summary", {"months": int(months.group("n"))}))
        elif patterns["agent_direct_month_now"].search(folded):
            found.append(DirectRoute("spending_summary", {"months": 1}))
    if patterns["agent_direct_compare"].search(folded):
        found.append(DirectRoute("compare_spending", {"months": 1}))
    return found[0] if len(found) == 1 else None  # hai công cụ cùng lúc: câu nhiều ý, để LLM quyết
