#!/usr/bin/env python3
"""Cổng chất lượng cho lớp luật quanh Rasa: luật ghi đè Rasa bao nhiêu câu, CỨU (Rasa sai, luật đúng) hay PHÁ (Rasa đúng, luật sai).

Vì sao có: lớp luật dễ biến thành chuỗi vá theo từng câu lỗi, rồi số đo trên chính những câu đã vá thì lạc quan. Mỗi thay đổi ở
``transaction_parser.py`` phải qua cổng này: tổng PHÁ không vượt CỨU, và luật không làm tụt bộ độc lập (``heldout_intent`` + ``nlu_test_fresh*``)
so với Rasa thô. Bộ độc lập là bộ chưa từng dùng để viết luật: KHÔNG sửa luật theo từng câu sai trong đó; thấy sai thì xem nguyên tắc nào thiếu.

Ba cách xử lý được so trên cùng câu: Rasa thô, Rasa + luật (đang chạy), luật thuần (không Rasa).

Chạy (cần Rasa đang chạy; mã nguồn gắn vào container ai-service, cùng mạng compose):
    docker run --rm -i --network finmate_be_default -v "<repo>/ai-service:/src:ro" -w /src -e PYTHONPATH=/src -e RASA_URL=http://rasa:5005 \
        --entrypoint python finmate_be-ai-service:latest scripts/audit_overrides.py [--strict]
(Git Bash trên Windows: đặt MSYS_NO_PATHCONV=1 để đường dẫn trong -v không bị đổi.)
"""
from __future__ import annotations

import argparse
import asyncio
import json
import os
import pathlib
import re
import sys
from datetime import datetime

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))

from app.services.nlu import RasaNlu  # noqa: E402
from app.services.transaction_parser import VN, parse_chat  # noqa: E402

NOW = datetime(2026, 10, 21, 10, 0, tzinfo=VN)
INDEPENDENT = ("heldout_intent", "nlu_test_fresh", "nlu_test_fresh2")  # chưa dùng để viết luật
SAME = {"needs_amount": "add_expense", "needs_period": "ask_spending"}  # luật hỏi lại số tiền = vẫn là khoản chi


def load_yaml_intents(path: pathlib.Path) -> list[tuple[str, str]]:
    rows, intent, in_examples = [], None, False
    for line in path.read_text(encoding="utf-8").splitlines():
        m = re.match(r"- intent: (\S+)", line)
        if m:
            intent, in_examples = m.group(1), False
        elif line.strip().startswith("examples:"):
            in_examples = True
        elif in_examples and line.startswith("    - "):
            rows.append((re.sub(r"\[([^\]]+)\]\([a-z_]+\)", r"\1", line[6:]).strip(), intent))
    return rows


def load_jsonl(path: pathlib.Path) -> list[tuple[str, str]]:
    return [(d["text"], d["intent"]) for d in map(json.loads, filter(None, path.read_text(encoding="utf-8").splitlines()))]


def intent_of(text: str, nlu: dict | None) -> str:
    got = parse_chat(text, nlu, now=NOW).intent
    return SAME.get(got, got)


def datasets() -> dict[str, list[tuple[str, str]]]:
    sets = {f.stem: load_yaml_intents(f) for f in sorted((ROOT / "rasa" / "tests").glob("nlu_test*.yml"))}
    heldout = ROOT / "tests" / "fixtures" / "eval" / "heldout_intent.jsonl"
    if heldout.exists():
        sets["heldout_intent"] = load_jsonl(heldout)
    return sets


async def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rasa-url", default=os.environ.get("RASA_URL", "http://rasa:5005"))
    parser.add_argument("--strict", action="store_true", help="thoát mã 1 nếu luật phá nhiều hơn cứu hoặc làm tụt bộ độc lập")
    parser.add_argument("--show", type=int, default=15, help="số ví dụ PHÁ/CỨU in ra")
    args = parser.parse_args()

    client = RasaNlu(args.rasa_url, timeout=10)
    total = dict(n=0, raw=0, final=0, rules=0, over=0, fixed=0, broke=0)
    broke_examples, fixed_examples, problems = [], [], []
    print(f"{'bộ':22}{'n':>5}{'Rasa thô':>10}{'Rasa+luật':>11}{'luật thuần':>12}{'ghi đè':>8}{'cứu':>6}{'phá':>6}")
    for name, rows in datasets().items():
        s = dict.fromkeys(total, 0)
        for text, label in rows:
            nlu = await client.parse(text)
            if nlu is None:
                raise SystemExit(f"Rasa không trả lời ở {args.rasa_url}; bật Rasa rồi chạy lại")
            raw, final, rules = nlu["intent"], intent_of(text, nlu), intent_of(text, None)
            s["n"] += 1
            s["raw"] += raw == label
            s["final"] += final == label
            s["rules"] += rules == label
            if final != raw:
                s["over"] += 1
                if final == label:
                    s["fixed"] += 1
                    fixed_examples.append((name, text, raw, final))
                elif raw == label:
                    s["broke"] += 1
                    broke_examples.append((name, text, label, final))
        print(f"{name:22}{s['n']:>5}{100 * s['raw'] / s['n']:>9.1f}%{100 * s['final'] / s['n']:>10.1f}%{100 * s['rules'] / s['n']:>11.1f}%{s['over']:>8}{s['fixed']:>6}{s['broke']:>6}")
        if name in INDEPENDENT and s["final"] < s["raw"]:
            problems.append(f"luật làm tụt bộ độc lập {name}: {s['raw']} -> {s['final']} câu đúng")
        for k in total:
            total[k] += s[k]
    t = total
    print(f"{'TỔNG':22}{t['n']:>5}{100 * t['raw'] / t['n']:>9.1f}%{100 * t['final'] / t['n']:>10.1f}%{100 * t['rules'] / t['n']:>11.1f}%{t['over']:>8}{t['fixed']:>6}{t['broke']:>6}")
    print(f"\nLuật ghi đè Rasa ở {t['over']}/{t['n']} câu ({100 * t['over'] / t['n']:.1f}%): cứu {t['fixed']}, phá {t['broke']}.")
    if t["broke"] > t["fixed"]:
        problems.append(f"luật phá ({t['broke']}) nhiều hơn cứu ({t['fixed']})")
    for title, items in (("PHÁ (Rasa đúng, luật sai): bộ, câu, nhãn, luật ra", broke_examples), ("CỨU (Rasa sai, luật đúng): bộ, câu, Rasa ra, luật ra", fixed_examples)):
        print(f"\n{title}")
        for item in items[: args.show]:
            print("  ", item)
    for p in problems:
        print("CẢNH BÁO:", p)
    return 1 if (args.strict and problems) else 0


if __name__ == "__main__":
    raise SystemExit(asyncio.run(main()))
