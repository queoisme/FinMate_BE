#!/usr/bin/env python3
"""Chấm bộ kiểm thử cố định cho chat và phân loại món (tests/fixtures/eval/*.jsonl).

Không dùng Rasa, DB hay Gemini thật: chat chạy bằng luật, món chạy với kiến thức rỗng và Gemini luôn trả "unknown",
nên số đo là của lớp luật/từ điển. Nhãn kỳ vọng được viết trước khi sửa luật và không được sửa theo kết quả.

Chạy:
    python scripts/eval_flows.py [--chat|--items|--consistency]
"""

from __future__ import annotations

import argparse
import asyncio
import json
import pathlib
import sys
from collections import Counter
from datetime import datetime

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from app.api.v1.chat import _lexicon_category, _slug  # noqa: E402
from app.bill.item_classifier import classify_items  # noqa: E402
from app.chat.parser import VN, parse_chat  # noqa: E402
from app.core.category_map import INCOME_SLUG  # noqa: E402
from app.schemas.bill import ClassifyItemIn  # noqa: E402

FIXTURES = pathlib.Path(__file__).resolve().parent.parent / "tests" / "fixtures" / "eval"
NOW = datetime(2026, 10, 12, 10, 0, tzinfo=VN)


def _load(name: str) -> list[dict]:
    return [json.loads(line) for line in (FIXTURES / name).read_text(encoding="utf-8").splitlines() if line.strip()]


def _report(rows: list[tuple[str, object, object, bool]]) -> dict:
    """rows: (nhãn mô tả, kỳ vọng, dự đoán, bỏ qua/không chắc). Sai mà không bỏ qua = tự tin sai."""
    failures = [(label, want, got) for label, want, got, _ in rows if want != got]
    wrong_confident = sum(1 for _, want, got, abstained in rows if want != got and not abstained)
    return {
        "total": len(rows),
        "correct": len(rows) - len(failures),
        "accuracy": round((len(rows) - len(failures)) / len(rows), 3),
        "abstain_rate": round(sum(1 for r in rows if r[3]) / len(rows), 3),
        "wrong_confident": wrong_confident,
        "failures": failures,
    }


def _entries(parsed) -> list:
    many = getattr(parsed, "entries", None)
    return list(many) if many else ([parsed.entry] if parsed.entry else [])


def _shape(entry) -> tuple:
    category = INCOME_SLUG if entry.kind == "income" else (None if entry.kind == "transfer" else _slug(entry.category))
    return (entry.kind, entry.amount, category)


def run_chat() -> dict:
    rows = []
    for case in _load("chat_cases.jsonl"):
        parsed = parse_chat(case["text"], None, now=NOW, fallback_category=_lexicon_category)
        if "intent" in case:
            rows.append((case["text"], case["intent"], parsed.intent, False))
            continue
        want = [(e["kind"], e["amount"], e["category"]) for e in case["expect"]]
        got = [_shape(e) for e in _entries(parsed)]
        abstained = any(k == "expense" and c is None for k, _, c in got)
        rows.append((case["text"], want, got, abstained))
    return _report(rows)


class _NoKnowledge:
    async def refresh_if_stale(self) -> None:
        return None

    async def cached_items(self, keys):
        return {}

    async def remember_items(self, answers) -> None:
        return None


class _SilentGemini:
    available = True

    async def classify(self, names, instruction):
        return ["unknown"] * len(names)


def run_items() -> dict:
    cases = _load("item_cases.jsonl")

    async def go():
        rows = []
        for case in cases:
            out, _ = await classify_items(
                [ClassifyItemIn(name=case["name"], parent=None)], case["merchant"], _NoKnowledge(), _SilentGemini(), "x", True
            )
            got = out[0].category_slug or "unknown"
            rows.append((f'{case["name"]} @ {case["merchant"]}', case["expect"], got, got == "unknown"))
        return rows

    return _report(asyncio.run(go()))


def run_consistency() -> dict:
    """Cùng một cụm từ đi bằng chat và bằng món trên bill có ra cùng danh mục không (và có đúng kỳ vọng không)."""
    cases = _load("consistency_cases.jsonl")

    async def item_slugs():
        out = {}
        for case in cases:
            result, _ = await classify_items(
                [ClassifyItemIn(name=case["phrase"], parent=None)], None, _NoKnowledge(), _SilentGemini(), "x", False
            )
            out[case["phrase"]] = result[0].category_slug or "unknown"
        return out

    item_by_phrase = asyncio.run(item_slugs())
    disagreements, agree, correct = [], 0, 0
    for case in cases:
        parsed = parse_chat(case["phrase"] + " 50k", None, now=NOW, fallback_category=_lexicon_category)
        chat = (_slug(parsed.entry.category) if parsed.entry else None) or "unknown"
        item = item_by_phrase[case["phrase"]]
        if chat == item:
            agree += 1
            correct += chat == case["expect"]
        else:
            disagreements.append((case["phrase"], chat, item, case["expect"]))
    return {
        "total": len(cases),
        "agree": agree,
        "agree_rate": round(agree / len(cases), 3),
        "correct": correct,
        "disagreements": disagreements,
    }


def _print_consistency(report: dict) -> None:
    print(f"== consistency: {report['agree']}/{report['total']} cùng danh mục ở chat và bill (agree_rate {report['agree_rate']}), "
          f"cùng và đúng kỳ vọng {report['correct']}")
    for phrase, chat, item, expect in report["disagreements"]:
        print(f"   - {phrase!r}: chat={chat} bill={item} kỳ vọng={expect}")


def _print(title: str, report: dict) -> None:
    print(f"== {title}: {report['correct']}/{report['total']} đúng, accuracy {report['accuracy']}, "
          f"bỏ qua {report['abstain_rate']}, sai mà tự tin {report['wrong_confident']}")
    confusion = Counter((str(w), str(g)) for _, w, g in report["failures"])
    for (want, got), n in confusion.most_common(8):
        print(f"   nhầm {want} -> {got}: {n}")
    for label, want, got in report["failures"]:
        print(f"   - {label!r}: muốn {want}, ra {got}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--chat", action="store_true")
    parser.add_argument("--items", action="store_true")
    parser.add_argument("--consistency", action="store_true")
    args = parser.parse_args()
    both = not (args.chat or args.items or args.consistency)
    if args.chat or both:
        _print("chat", run_chat())
    if args.items or both:
        _print("items", run_items())
    if args.consistency or both:
        _print_consistency(run_consistency())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
