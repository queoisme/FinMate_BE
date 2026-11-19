"""Đo khả năng hiểu câu ĐẶT MỤC TIÊU qua đường thật của chat: chỉ luật, và luật + AI đọc hộ thời hạn (Gemini thật).

    cd ai-service && python scripts/eval_goal.py [--ai] [--timeout 4] [--show-wrong]

Bộ câu: tests/fixtures/eval/goal_cases.jsonl (nhãn do người viết, "hôm nay" cố định 19/11/2026 16:42 giờ Việt Nam). Mỗi câu có: đây là mục tiêu hay không, số tiền, hạn (months | date) và từ khóa của tên.
Chỉ ĐỌC: không ghi tệp nào. Với --ai cần khóa Gemini (chạy trong container ai-service) và gọi Gemini thật, mỗi câu mục tiêu thiếu hạn một lượt.

Chỉ số quan trọng nhất là "tự tạo sai": câu mà app SẼ TỰ TẠO mục tiêu (luật đọc, không ai_assisted) nhưng số tiền hoặc hạn sai. AI đọc hộ thì app không tự tạo nên không tính vào đó.
"""

from __future__ import annotations

import argparse
import asyncio
import json
import sys
import time
from datetime import date, datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from app.services.chat_ai import apply_ai_goal  # noqa: E402
from app.services.goal_parser import deadline_after  # noqa: E402
from app.services.intent_classifier import IntentClassifier, load_examples  # noqa: E402
from app.services.transaction_parser import VN, parse_chat  # noqa: E402
from app.services.vi_text import fold  # noqa: E402

NOW = datetime(2026, 11, 19, 16, 42, tzinfo=VN)
CASES = [json.loads(line) for line in (ROOT / "tests" / "fixtures" / "eval" / "goal_cases.jsonl").read_text(encoding="utf-8").splitlines() if line.strip()]


def expected_deadline(case: dict) -> date | None:
    if "months" in case:
        return deadline_after(case["months"], "month", NOW).date()
    return date.fromisoformat(case["date"]) if "date" in case else None


def judge(case: dict, parsed) -> dict:
    """Kết quả một câu: nhận ra mục tiêu / số tiền / hạn / tên đúng không, và app có tự tạo không."""
    goal = parsed.goal
    detected = parsed.intent == "create_goal" and goal is not None
    if not case["goal"]:
        return {"detected": detected, "ok": not detected, "auto_wrong": detected and not goal.ai_assisted}
    if not detected:
        return {"detected": False, "ok": False, "auto_wrong": False}
    amount_ok = goal.target == case["target"]
    deadline_ok = (goal.deadline.date() if goal.deadline else None) == expected_deadline(case)
    key = case.get("name")
    name_ok = key is None or key in fold(goal.name)
    auto = goal.deadline is not None and not goal.ai_assisted
    return {"detected": True, "amount": amount_ok, "deadline": deadline_ok, "name": name_ok, "ok": amount_ok and deadline_ok and name_ok, "auto_wrong": auto and not (amount_ok and deadline_ok)}


async def run(mode_ai: bool, timeout: float, classifier: IntentClassifier, gemini) -> tuple[list[dict], list[float]]:
    rows, latencies = [], []
    for case in CASES:
        text = case["text"]
        intent, confidence = classifier.predict(text)
        parsed = parse_chat(text, {"intent": intent, "confidence": confidence, "entities": {}}, now=NOW)
        started = time.perf_counter()
        if mode_ai:
            stats: dict[str, int] = {}

            async def reparse(fixed: str):
                fixed_intent, fixed_confidence = classifier.predict(fixed)
                return parse_chat(fixed, {"intent": fixed_intent, "confidence": fixed_confidence, "entities": {}}, now=NOW)

            # Mỗi câu một client mới: một lượt chậm làm cầu dao của client mở 30 giây và các câu sau bị bỏ qua, che mất khả năng thật của AI. Production vẫn dùng một client chung (có cầu dao).
            parsed = await apply_ai_goal(text, parsed, reparse, gemini(), True, NOW, timeout=timeout, stats=stats)
            if stats:
                latencies.append(time.perf_counter() - started)
            case = {**case, "_ai": stats}
        rows.append({"case": case, "parsed": parsed, **judge(case, parsed)})
    return rows, latencies


def report(title: str, rows: list[dict], latencies: list[float], show_wrong: bool) -> None:
    goals = [r for r in rows if r["case"]["goal"]]
    negatives = [r for r in rows if not r["case"]["goal"]]
    ok = sum(r["ok"] for r in goals)
    detected = sum(r["detected"] for r in goals)
    print(f"\n== {title} ==")
    print(f"câu mục tiêu: đúng hoàn toàn {ok}/{len(goals)} ({ok / len(goals):.0%}); nhận ra là mục tiêu {detected}/{len(goals)}")
    print(f"  số tiền đúng {sum(bool(r.get('amount')) for r in goals)}/{len(goals)}; hạn đúng {sum(bool(r.get('deadline')) for r in goals)}/{len(goals)}; tên đúng {sum(bool(r.get('name')) for r in goals)}/{len(goals)}")
    print(f"câu KHÔNG phải mục tiêu: đúng {sum(r['ok'] for r in negatives)}/{len(negatives)}")
    wrong_auto = [r for r in rows if r["auto_wrong"]]
    print(f"TỰ TẠO SAI (app sẽ tự tạo mục tiêu sai tiền hoặc hạn): {len(wrong_auto)}")
    if latencies:
        ordered = sorted(latencies)
        print(f"  bước AI chạy {len(latencies)} câu; trung vị {ordered[len(ordered) // 2]:.1f}s, chậm nhất {ordered[-1]:.1f}s; nhận ra mục tiêu hộ {sum(bool(r['case'].get('_ai', {}).get('goal_detected')) for r in rows)} câu, đọc hạn hộ {sum(bool(r['case'].get('_ai', {}).get('goal')) for r in rows)} câu, lỗi/hết giờ {sum(bool(r['case'].get('_ai', {}).get('goal_failed')) for r in rows)}")
    if show_wrong:
        for r in rows:
            if not r["ok"] or r["auto_wrong"]:
                goal = r["parsed"].goal
                got = f"{r['parsed'].intent}" if goal is None else f"tên={goal.name!r} tiền={goal.target} hạn={goal.deadline.date() if goal.deadline else None} ai={goal.ai_assisted}"
                want = "không phải mục tiêu" if not r["case"]["goal"] else f"tiền={r['case']['target']} hạn={expected_deadline(r['case'])}"
                print(f"  SAI{' [TỰ TẠO]' if r['auto_wrong'] else ''}: {r['case']['text']!r}\n       đọc: {got}\n       cần: {want}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--ai", action="store_true", help="đo thêm chế độ luật + AI đọc hộ hạn (Gemini thật)")
    parser.add_argument("--timeout", type=float, default=8.0, help="giây chờ Gemini mỗi câu (mặc định như production: settings.chat_goal_timeout_seconds)")
    parser.add_argument("--show-wrong", action="store_true")
    args = parser.parse_args()

    classifier = IntentClassifier.train(load_examples(splits=("train",)), c=20.0, class_weight="balanced")
    rows, _ = asyncio.run(run(False, 0, classifier, None))
    report("CHỈ LUẬT", rows, [], args.show_wrong)
    if args.ai:
        from app.api.v1.bill import get_chat_gemini_client

        from app.api.v1.bill import LlmClient, secret_store, settings

        def fresh():
            return LlmClient(
                secret_store.getter("gemini_api_key"),
                settings.chat_gemini_model,
                settings.chat_gemini_timeout_seconds,
                overall_timeout=settings.chat_gemini_overall_seconds,
                fallback_models=tuple(m.strip() for m in settings.chat_gemini_fallback_models.split(",") if m.strip()),
                health_channel="chat",
            )

        rows, latencies = asyncio.run(run(True, args.timeout, classifier, fresh))
        report(f"LUẬT + AI ĐỌC HỘ HẠN (chờ tối đa {args.timeout:g}s)", rows, latencies, args.show_wrong)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
