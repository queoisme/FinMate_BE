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


def _runner(fail_ids=()):
    import httpx

    calls = []

    def runner(question, index):
        calls.append(question["id"])
        if question["id"] in fail_ids:
            raise httpx.ConnectError("llm down")
        return {"id": question["id"], "kind": question["kind"], "ok": True, "index": index}

    runner.calls = calls
    return runner


def _questions(n=3):
    return [{"id": f"q{i}", "kind": "forecast"} for i in range(n)]


def test_a_failing_question_is_recorded_as_errored_and_the_run_continues(tmp_path):
    path = tmp_path / "r.jsonl"

    rows, errored = run.run_questions(_questions(), _runner({"q1"}), path)

    assert [r["id"] for r in rows] == ["q0", "q2"]
    assert errored == ["q1"]
    lines = [__import__("json").loads(x) for x in path.read_text(encoding="utf-8").splitlines()]
    assert [x["id"] for x in lines] == ["q0", "q1", "q2"] and "error" in lines[1]


def test_resume_skips_answered_ids_retries_errored_ones_and_keeps_question_indices(tmp_path):
    path = tmp_path / "r.jsonl"
    run.run_questions(_questions(), _runner({"q1"}), path)

    second = _runner()
    rows, errored = run.run_questions(_questions(), second, path)

    assert second.calls == ["q1"]  # chỉ chạy lại câu lỗi
    assert errored == [] and [r["id"] for r in rows] == ["q0", "q1", "q2"]
    assert next(r for r in rows if r["id"] == "q1")["index"] == 1  # hồ sơ giả lập theo vị trí gốc, không đổi khi resume


def test_every_answered_row_is_on_disk_before_the_next_question_runs(tmp_path):
    path = tmp_path / "r.jsonl"
    seen = []

    def runner(question, index):
        seen.append(len(path.read_text(encoding="utf-8").splitlines()) if path.exists() else 0)
        return {"id": question["id"], "kind": "forecast"}

    run.run_questions(_questions(), runner, path)

    assert seen == [0, 1, 2]


def test_the_runner_accepts_resume_and_rejects_an_unknown_run_id():
    import pytest

    with pytest.raises(SystemExit):
        run.main(["--resume", "../escape"])


def test_retrieved_comes_from_document_evidence_in_order_without_duplicates_and_relevant_from_the_label():
    question = {**run.load_questions(1)[0], "expect": {**run.load_questions(1)[0]["expect"], "relevant_sources": ["Phương pháp 50/30/20"]}}
    body = payload()
    body["answer"]["evidence"] = [
        {"id": "d1", "valueVnd": None, "source": "document", "sourceName": "Phương pháp 50/30/20"},
        {"id": "b", "valueVnd": 1_000, "source": "confirmed", "sourceName": None},
        {"id": "d2", "valueVnd": None, "source": "document", "sourceName": "Quỹ khẩn cấp"},
        {"id": "d3", "valueVnd": None, "source": "document", "sourceName": "Phương pháp 50/30/20"},
    ]

    row = run.build_row(question, body, 10, "m")

    assert row["retrieved"] == ["Phương pháp 50/30/20", "Quỹ khẩn cấp"]
    assert row["relevant"] == ["Phương pháp 50/30/20"]


def test_a_question_without_relevant_sources_has_an_empty_relevant_list_and_the_summary_reports_na():
    question = run.load_questions(1)[0]
    rows = [run.build_row(question, payload(), 10, "m")]

    assert rows[0]["relevant"] == [] and rows[0]["retrieved"] == []
    assert run.summarize(rows, {})["retrieval_recall_at_5"] is None


def test_pause_seconds_sleeps_between_questions_that_actually_run_but_not_for_resumed_ones(tmp_path, monkeypatch):
    sleeps = []
    monkeypatch.setattr(run.time, "sleep", lambda seconds: sleeps.append(seconds))
    path = tmp_path / "r.jsonl"

    run.run_questions(_questions(3), _runner(), path, pause_seconds=2.5)
    assert sleeps == [2.5, 2.5]  # giữa 3 câu có 2 khoảng nghỉ, không nghỉ sau câu cuối

    sleeps.clear()
    run.run_questions(_questions(3), _runner(), path, pause_seconds=2.5)  # tất cả đã trả lời: không chạy gì thì không nghỉ
    assert sleeps == []
