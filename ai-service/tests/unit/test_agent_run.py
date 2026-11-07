"""Bộ chạy đánh giá (Task 12): gói phản hồi của endpoint thành hàng chấm điểm, và thiếu bảng giá thì báo n/a thay vì $0."""

from agent_eval import run


def payload(status="answered", tool_status="Ok"):
    return {
        "answer": {
            "status": status,
            "claims": [{"text": "Cần thêm 500.000 đồng", "evidenceIds": ["gap"]}],
            "evidence": [{"id": "gap", "valueVnd": 500_000}],
            "usedTemplate": False,
        },
        "trace": [{"tool": "forecast_goal", "status": tool_status}, {"tool": "get_assumptions", "status": "Rejected"}],
        "usage": {"inputTokens": 150, "outputTokens": 30},
    }


def test_build_row_keeps_only_tools_that_ran_ok():
    question = run.load_questions(1)[0]

    row = run.build_row(question, payload(), 250, "m")

    assert row["tools"] == ["forecast_goal"]
    assert row["claims"] == [{"text": "Cần thêm 500.000 đồng", "evidence_ids": ["gap"]}]
    assert row["evidence"] == [{"id": "gap", "value_vnd": 500_000}]
    assert row["usage"] == {"input_tokens": 150, "output_tokens": 30, "model": "m"}
    assert row["latency_ms"] == 250 and row["id"] == question["id"]


def test_summary_reports_cost_as_none_when_the_model_has_no_price():
    question = run.load_questions(1)[0]
    rows = [run.build_row(question, payload(), 100, "m")]

    assert run.summarize(rows, {})["cost_usd"] is None
    priced = run.summarize(rows, {"m": {"input_per_million": 1.0, "output_per_million": 2.0}})
    assert priced["cost_usd"] == (150 * 1.0 + 30 * 2.0) / 1_000_000
    assert priced["unsupported_claim_rate"] == 0.0 and priced["questions"] == 1


def test_markdown_table_has_one_line_per_metric():
    table = run.markdown_table("full", {"plan_accuracy": 0.91234, "cost_usd": None})

    assert "| plan_accuracy | 0.9123 |" in table and "| cost_usd | n/a |" in table


def test_the_runner_arguments_reject_an_unknown_config():
    import pytest

    with pytest.raises(SystemExit):
        run.main(["--config", "nope"])
