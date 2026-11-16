"""Báo cáo câu sai của eval_intent: nhóm theo cặp nhãn, cặp sai nhiều nhất đứng trước, mỗi dòng có đủ nhãn mong đợi, nhãn đoán và văn bản."""

import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2] / "scripts"))
import eval_intent  # noqa: E402


def test_wrong_cases_are_grouped_by_label_pair_most_frequent_first():
    wrong = [
        ("lương về 15 triệu", "add_income", "add_expense"),
        ("mới mua giày 1tr2", "add_expense", "needs_amount"),
        ("công ty chuyển lương 9tr", "add_income", "add_expense"),
    ]

    lines = eval_intent.format_wrong(wrong)

    assert lines[0] == "add_income -> add_expense (2)"
    assert "  lương về 15 triệu" in lines
    assert "  công ty chuyển lương 9tr" in lines
    assert lines.index("add_expense -> needs_amount (1)") > lines.index("  công ty chuyển lương 9tr")
    assert "  mới mua giày 1tr2" in lines


def test_no_wrong_cases_gives_no_lines():
    assert eval_intent.format_wrong([]) == []
