"""Đường tắt của agent: câu quen về dữ liệu quản lý được luật chọn công cụ, không qua LLM lập kế hoạch (app/agent/direct.py)."""

import pytest

from app.agent.direct import direct_route


@pytest.mark.parametrize(
    ("text", "tool", "args"),
    [
        ("ngân sách ăn uống còn bao nhiêu", "budget_status", {}),
        ("còn bao nhiêu ngân sách", "budget_status", {}),
        ("hạn mức tháng này đã dùng hết chưa", "budget_status", {}),
        ("mục tiêu của tôi đến đâu rồi", "goals_progress", {}),
        ("tiến độ các mục tiêu thế nào", "goals_progress", {}),
        ("sắp đến hạn khoản nào", "upcoming_items", {}),
        ("hóa đơn nào sắp tới", "upcoming_items", {}),
        ("tháng này tiêu nhiều nhất vào đâu", "spending_summary", {"months": 1}),
        ("3 tháng qua mình chi vào đâu nhiều nhất", "spending_summary", {"months": 3}),
        ("thang nay toi tieu nhieu nhat vao dau", "spending_summary", {"months": 1}),
        ("so sánh chi tiêu tháng này với tháng trước", "compare_spending", {"months": 1}),
        ("so sanh chi tieu thang truoc voi thang nay", "compare_spending", {"months": 1}),
    ],
)
def test_familiar_questions_pick_their_tool_without_the_llm(text, tool, args):
    route = direct_route(text)

    assert route is not None
    assert (route.tool, route.args) == (tool, args)


@pytest.mark.parametrize(
    "text",
    [
        "tiêu nhiều nhất vào đâu",  # không nói kỳ: để LLM quyết
        "20 tháng qua tôi chi vào đâu nhiều nhất",  # ngoài khoảng 1-12 tháng của công cụ
        "đặt hạn mức ăn uống 3 triệu",  # có số tiền: lệnh, không phải câu hỏi tình hình
        "nếu tôi tiết kiệm 5 triệu thì ngân sách còn bao nhiêu",  # câu kế hoạch
        "ngân sách còn bao nhiêu và mục tiêu đến đâu rồi",  # hai công cụ cùng lúc
        "mua xe 600 triệu sau 2 năm được không",
        "chào bạn",
    ],
)
def test_anything_unclear_goes_to_the_planner(text):
    assert direct_route(text) is None
