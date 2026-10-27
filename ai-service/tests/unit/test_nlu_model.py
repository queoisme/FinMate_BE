"""scripts/nlu_model.py: số đo thô vs sau luật, cổng chặn promote, mã băm tập dữ liệu (không cần DB, Docker hay Rasa)."""

import sys
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))

import nlu_examples as examples  # noqa: E402
import nlu_model as model  # noqa: E402


def result(label, raw, final, conf=0.9, tag="t"):
    return {"tag": tag, "label": label, "raw": raw, "conf": conf, "final": final}


def test_summary_keeps_raw_and_after_rules_apart_and_counts_rescues_and_breaks():
    results = [
        result("a", "a", "a"),  # cả hai đúng
        result("a", "b", "a", conf=0.99),  # Rasa sai (sai mà bão hòa), luật cứu
        result("a", "a", "b"),  # Rasa đúng, luật phá
        result("a", "b", "b", conf=0.85),  # cả hai sai (sai mà chắc)
    ]
    raw, final = model.summarize(results, "hash")
    assert (raw["n"], raw["acc"], final["acc"]) == (4, 0.5, 0.5)
    assert (raw["wrong_confident"], raw["wrong_saturated"]) == (2, 1)
    assert (final["overrides"], final["fixed"], final["broke"]) == (2, 1, 1)
    assert raw["per_intent"]["a"] == {"n": 4, "ok": 2} and raw["test_hash"] == "hash"


def ev(raw_acc, final_acc, *, test_hash="h", fixed=1, broke=0, tags=None):
    by_tag = {tag: {"n": 10, "ok": round(10 * acc), "acc": acc} for tag, acc in (tags or {}).items()}
    return {
        "eval_raw": {"acc": raw_acc, "test_hash": test_hash, "by_tag": by_tag},
        "eval_final": {"acc": final_acc, "test_hash": test_hash, "fixed": fixed, "broke": broke, "by_tag": by_tag},
    }


def test_promotion_needs_a_measurement():
    assert model.decide_promotion({"eval_raw": None, "eval_final": None}, None)


def test_first_model_can_be_promoted_without_an_active_one_unless_rules_break_more_than_they_fix():
    assert model.decide_promotion(ev(0.9, 0.95), None) == []
    assert model.decide_promotion(ev(0.9, 0.95, fixed=1, broke=2), None)


def test_cannot_compare_against_an_active_model_that_was_never_measured():
    assert model.decide_promotion(ev(0.9, 0.95), {"eval_raw": None, "eval_final": None})


def test_cannot_compare_when_the_test_set_changed():
    reasons = model.decide_promotion(ev(0.99, 0.99, test_hash="new"), ev(0.9, 0.9, test_hash="old"))
    assert reasons and "tập test" in reasons[0]


def test_worse_after_rules_or_raw_is_blocked_even_if_the_other_improves():
    assert model.decide_promotion(ev(0.95, 0.90), ev(0.90, 0.92))  # thô tốt lên nhưng cuối tụt
    assert model.decide_promotion(ev(0.88, 0.95), ev(0.90, 0.92))  # cuối tốt lên chỉ vì luật gánh: thô tụt


def test_a_drop_on_an_independent_set_blocks_promotion():
    active = ev(0.90, 0.92, tags={"nlu_test_fresh": 0.9, "nlu_test_v2": 0.9})
    assert model.decide_promotion(ev(0.91, 0.93, tags={"nlu_test_fresh": 0.8, "nlu_test_v2": 1.0}), active)
    assert model.decide_promotion(ev(0.91, 0.93, tags={"nlu_test_fresh": 0.9, "nlu_test_v2": 0.5}), active) == []  # bộ máy sinh tụt thì không chặn


def test_equal_or_better_is_promoted():
    assert model.decide_promotion(ev(0.90, 0.92), ev(0.90, 0.92)) == []
    assert model.decide_promotion(ev(0.92, 0.95), ev(0.90, 0.92)) == []


def row(intent, text, split="train", tag=None):
    return SimpleNamespace(intent=intent, text=text, text_key=text.lower(), split=split, tag=tag)


def test_dataset_hash_depends_on_content_not_on_row_order_or_tags():
    rows = [row("greet", "chào"), row("add_expense", "ăn phở [45k](amount)"), row("out_of_scope", "mấy giờ rồi", split="test")]
    digest, counts = examples.snapshot_of(rows)
    assert digest == examples.snapshot_of(list(reversed(rows)))[0]
    assert digest == examples.snapshot_of([row("greet", "chào", tag="x"), rows[1], rows[2]])[0]
    assert digest == examples.snapshot_of(rows[:2] + [row("out_of_scope", "câu test khác", split="test")])[0]  # tập test không nằm trong mã băm train
    assert digest != examples.snapshot_of([row("greet", "chào bạn"), rows[1], rows[2]])[0]
    assert digest != examples.snapshot_of([row("goodbye", "chào"), rows[1], rows[2]])[0]
    assert counts == {"train": {"greet": 1, "add_expense": 1}, "test": {"out_of_scope": 1}}


def test_test_set_hash_changes_when_a_sentence_is_added():
    base = [row("greet", "chào", split="test")]
    assert model.hash_of_test_set(base) == model.hash_of_test_set(list(base))
    assert model.hash_of_test_set(base) != model.hash_of_test_set([*base, row("greet", "hello", split="test")])
