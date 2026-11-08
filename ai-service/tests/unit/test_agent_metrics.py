"""Chỉ số đánh giá agent (Task 12): mỗi hàm chấm đúng trên ví dụ nhỏ có đáp án tính tay."""

import pytest

from agent_eval import metrics


def row(*, in_scope=True, clarify=False, tools=(), status="answered", used=(), claims=(), evidence=(), latency=100, **extra):
    return {
        "expect": {"in_scope": in_scope, "clarify": clarify, "tools": list(tools)},
        "status": status,
        "tools": list(used),
        "claims": [{"text": t, "evidence_ids": list(ids)} for t, ids in claims],
        "evidence": [{"id": i, "value_vnd": v} for i, v in evidence],
        "latency_ms": latency,
        **extra,
    }


def test_unsupported_claim_rate_counts_only_unverified():
    rows = [
        row(
            claims=[
                ("Bạn cần thêm 500.000 đồng mỗi tháng", ["gap"]),  # đúng: khớp bằng chứng
                ("Bạn cần thêm 600.000 đồng mỗi tháng", ["gap"]),  # sai số
                ("Chắc chắn mua được", []),  # không bằng chứng
                ("Bạn định mua món giá bao nhiêu?", []),  # câu hỏi lại: hợp lệ
            ],
            evidence=[("gap", 500_000)],
        )
    ]

    assert metrics.unsupported_claim_rate(rows) == pytest.approx(2 / 4)


def test_unsupported_claim_rate_is_zero_without_claims():
    assert metrics.unsupported_claim_rate([row(status="refused")]) == 0.0


def test_numeric_faithfulness_is_the_share_of_amounts_matching_cited_evidence():
    rows = [row(claims=[("Cần 500.000 đồng và 700.000 đồng", ["gap"])], evidence=[("gap", 500_000)])]

    assert metrics.numeric_faithfulness(rows) == pytest.approx(0.5)
    assert metrics.numeric_faithfulness([row()]) == 1.0  # không có số tiền nào để sai


def test_refusal_recall_vs_precision_known_example():
    rows = [
        row(in_scope=False, status="refused"),  # từ chối đúng
        row(in_scope=False, status="answered"),  # lọt: hụt recall
        row(in_scope=True, status="refused"),  # từ chối nhầm: hụt precision
        row(in_scope=True, status="answered"),
    ]

    assert metrics.refusal_recall(rows) == pytest.approx(1 / 2)
    assert metrics.refusal_precision(rows) == pytest.approx(1 / 2)


def test_plan_accuracy_compares_refuse_clarify_answer():
    rows = [
        row(in_scope=False, status="refused"),
        row(clarify=True, status="clarify"),
        row(status="answered"),
        row(status="clarify"),  # mong đợi trả lời nhưng hỏi lại
    ]

    assert metrics.plan_accuracy(rows) == pytest.approx(3 / 4)


def test_insufficient_data_counts_as_an_answer_attempt():
    assert metrics.plan_accuracy([row(status="insufficient_data")]) == 1.0


def test_tool_accuracy_needs_the_exact_tool_set():
    rows = [
        row(tools=["forecast_goal"], used=["forecast_goal"]),
        row(tools=["forecast_goal"], used=[]),
        row(tools=[], used=["get_assumptions"]),
        row(tools=[], used=[]),
    ]

    assert metrics.tool_accuracy(rows) == pytest.approx(2 / 4)


def test_retrieval_recall_at_k():
    rows = [
        row(relevant=["a", "b"], retrieved=["a", "x", "b"]),
        row(relevant=["c"], retrieved=["x", "y", "c"]),
        row(),  # không có đáp án truy xuất: bỏ qua
    ]

    assert metrics.retrieval_recall_at_k(rows, 1) == pytest.approx((1 / 2 + 0) / 2)
    assert metrics.retrieval_recall_at_k(rows, 3) == 1.0


def test_latency_percentiles():
    rows = [row(latency=v) for v in (100, 200, 300, 400, 1000)]

    p50, p95 = metrics.latency_p50_p95(rows)

    assert p50 == 300
    assert p95 == pytest.approx(880)


def test_cost_uses_price_table_not_hardcoded():
    usage = {"model": "m", "input_tokens": 2_000_000, "output_tokens": 500_000}

    assert metrics.cost_usd(usage, {"m": {"input_per_million": 0.1, "output_per_million": 0.4}}) == pytest.approx(0.4)
    assert metrics.cost_usd(usage, {"m": {"input_per_million": 1.0, "output_per_million": 2.0}}) == pytest.approx(3.0)
    with pytest.raises(KeyError):
        metrics.cost_usd(usage, {})


def test_every_price_entry_has_a_source_url_a_date_and_positive_rates():
    import json
    from agent_eval.run import PRICES

    table = json.loads(PRICES.read_text(encoding="utf-8"))
    if not table:
        pytest.skip("prices.json is still empty")
    for model, price in table.items():
        assert price["input_per_million"] > 0 and price["output_per_million"] > 0, model
        assert price["source_url"].startswith("https://"), model
        assert len(price["as_of"]) == 10, model  # YYYY-MM-DD
