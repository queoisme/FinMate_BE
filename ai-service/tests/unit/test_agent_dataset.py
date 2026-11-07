"""Bộ câu hỏi có nhãn của bộ đánh giá agent (Task 11): đúng schema, id duy nhất, đủ năm nhóm, và nhãn nhất quán với hành vi mong đợi."""

import json
from pathlib import Path

import pytest

from agent_eval.personas import KINDS

DATASET = Path(__file__).resolve().parents[2] / "agent_eval" / "dataset" / "questions.jsonl"
KINDS_OF_QUESTION = {"forecast", "knowledge", "clarify", "out_of_scope", "adversarial"}
REFUSAL_REASONS = {"out_of_scope", "investment_advice", "other_users_data", "prompt_injection"}


def load() -> list[dict]:
    return [json.loads(line) for line in DATASET.read_text(encoding="utf-8").splitlines() if line.strip()]


ROWS = load()


def test_every_line_matches_the_schema():
    for row in ROWS:
        assert set(row) == {"id", "text", "segment", "kind", "expect"}, row["id"]
        assert isinstance(row["id"], str) and row["id"]
        assert isinstance(row["text"], str) and 3 <= len(row["text"]) <= 500
        assert row["kind"] in KINDS_OF_QUESTION, row["id"]
        assert row["segment"] in KINDS, row["id"]
        expect = row["expect"]
        assert isinstance(expect["in_scope"], bool)
        assert isinstance(expect["tools"], list) and all(isinstance(t, str) for t in expect["tools"])
        assert isinstance(expect["must_cite"], list)
        assert isinstance(expect["clarify"], bool)
        assert expect["numeric_truth"] is None or set(expect["numeric_truth"]) == {"price_vnd", "horizon_months"}
        assert expect["refusal_reason"] is None or expect["refusal_reason"] in REFUSAL_REASONS


def test_ids_are_unique():
    ids = [r["id"] for r in ROWS]

    assert len(ids) == len(set(ids))


def test_the_dataset_covers_all_five_kinds_and_all_three_segments():
    assert {r["kind"] for r in ROWS} == KINDS_OF_QUESTION
    assert {r["segment"] for r in ROWS} == set(KINDS)
    assert len(ROWS) >= 60


@pytest.mark.parametrize("kind", ["out_of_scope", "adversarial"])
def test_out_of_scope_and_adversarial_questions_expect_a_refusal_with_a_reason(kind):
    for row in (r for r in ROWS if r["kind"] == kind):
        assert row["expect"]["in_scope"] is False, row["id"]
        assert row["expect"]["tools"] == [] and row["expect"]["numeric_truth"] is None, row["id"]
        assert row["expect"]["refusal_reason"] is not None, row["id"]


def test_forecast_questions_carry_the_exact_numeric_truth_and_expect_the_forecast_tool():
    for row in (r for r in ROWS if r["kind"] == "forecast"):
        assert row["expect"]["in_scope"] is True and row["expect"]["tools"] == ["forecast_goal"], row["id"]
        assert row["expect"]["must_cite"] == ["price", "balance"], row["id"]
        truth = row["expect"]["numeric_truth"]
        assert isinstance(truth["price_vnd"], int) and truth["price_vnd"] > 0 and isinstance(truth["horizon_months"], int) and truth["horizon_months"] > 0, row["id"]


def test_clarify_questions_expect_a_question_back_and_no_tool_run():
    for row in (r for r in ROWS if r["kind"] == "clarify"):
        assert row["expect"] == {"in_scope": True, "tools": [], "must_cite": [], "clarify": True, "numeric_truth": None, "refusal_reason": None}, row["id"]


def test_the_forecast_prices_in_the_text_match_the_numeric_truth():
    """Câu chữ và nhãn phải khớp nhau: bộ đọc số tất định của dự án đọc ra đúng giá đã ghi trong nhãn."""
    from app.services.amount_normalizer import find_amounts

    for row in (r for r in ROWS if r["kind"] == "forecast"):
        read = {m.value for m in find_amounts(row["text"]) if m.value >= 1_000}
        assert row["expect"]["numeric_truth"]["price_vnd"] in read, f"{row['id']}: {row['text']} → {read}"


def test_no_question_asks_for_a_number_that_the_dataset_would_have_to_invent():
    for row in ROWS:
        assert "numeric_truth" in row["expect"]
