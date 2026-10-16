"""Báo cáo đo bộ category-classifier: tách nhãn thật khỏi dữ liệu tổng hợp, và nói thẳng khi không đủ dữ liệu để đo."""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))

from eval_category_dataset import evaluate, real_grouped_cv, real_only, synthetic_only  # noqa: E402


def row(text, label, merchant="aaa store", by="user"):
    return {"text": text, "label": label, "merchant_key": merchant, "labeled_by": by}


TRAIN = [row("zqaaa zqaaa", "food"), row("zqbbb zqbbb", "transport"), row("zqaaa", "food"), row("zqbbb", "transport")]


def test_evaluate_scores_model_rules_and_confident_subset():
    result = evaluate(TRAIN, [row("zqaaa", "food"), row("zqbbb", "transport")])
    assert result["n"] == 2 and result["model"] == 1.0
    assert result["rules"] == 0.0  # luật không biết từ vô nghĩa
    assert 0 <= result["coverage_at_threshold"] <= 1


def test_evaluate_says_so_when_there_is_nothing_to_learn_or_measure():
    assert "note" in evaluate([], [row("x", "food")])
    assert "note" in evaluate([row("x", "food"), row("y", "food")], [row("x", "food")])  # chỉ một nhóm
    assert "note" in evaluate(TRAIN, [])


def test_real_and_synthetic_are_separated():
    rows = [row("a", "food", by="user"), row("b", "food", by="synthetic_ocr"), row("c", "food", by="gold"), row("d", "food", by="imported")]
    assert [r["text"] for r in real_only(rows)] == ["a", "c", "d"]
    assert [r["text"] for r in synthetic_only(rows)] == ["b"]


def test_real_grouped_cv_refuses_to_split_a_handful_of_shops():
    result = real_grouped_cv([row("a", "food", merchant=f"s{i}") for i in range(3)])
    assert result["n"] == 3 and "note" in result
