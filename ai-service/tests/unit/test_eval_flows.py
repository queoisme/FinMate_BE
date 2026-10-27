"""Bộ kiểm thử cố định cho chat và phân loại món: không được sửa nhãn kỳ vọng để một lần chạy qua."""

import json
import pathlib
import sys

from app.core.enums import CATEGORY_SLUGS

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2] / "scripts"))
import eval_flows  # noqa: E402

FIXTURES = pathlib.Path(__file__).resolve().parents[1] / "fixtures" / "eval"
VALID = set(CATEGORY_SLUGS) | {"loan", "unknown", None}  # "loan": danh mục của chat (cho vay/mượn/trả nợ), không thuộc CATEGORY_SLUGS


def lines(name):
    return [json.loads(line) for line in (FIXTURES / name).read_text(encoding="utf-8").splitlines() if line.strip()]


def test_fixture_lines_are_valid_json_and_slugs_exist():
    for case in lines("chat_cases.jsonl"):
        assert "text" in case and ("expect" in case) != ("intent" in case)
        for entry in case.get("expect", []):
            assert entry["kind"] in {"expense", "income", "transfer"}
            assert entry["category"] in VALID and isinstance(entry["amount"], int)
    for case in lines("item_cases.jsonl"):
        assert case["expect"] in VALID and case["name"]


def test_runner_reports_metrics_for_both_sets():
    chat = eval_flows.run_chat()
    items = eval_flows.run_items()
    for report in (chat, items):
        assert report["total"] > 0 and 0 <= report["accuracy"] <= 1 and 0 <= report["abstain_rate"] <= 1
        assert len(report["failures"]) == report["total"] - report["correct"]


def test_consistency_fixture_is_valid_and_runner_reports():
    cases = lines("consistency_cases.jsonl")
    assert len(cases) >= 50
    assert all(case["expect"] in (set(CATEGORY_SLUGS) - {"income"}) | {"unknown"} for case in cases)
    report = eval_flows.run_consistency()
    assert report["total"] == len(cases)
    assert 0 <= report["agree_rate"] <= 1 and report["agree"] <= report["total"] and report["correct"] <= report["total"]
    assert len(report["disagreements"]) == report["total"] - report["agree"]
