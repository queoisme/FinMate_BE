"""Đo bộ phân loại ý định (app/services/intent_classifier.py) qua ĐƯỜNG THẬT của chat: parse_chat(text, nlu) so với nhãn mong đợi.

    cd ai-service && python scripts/eval_intent.py [--c 20] [--holdout-only]

Mặc định huấn luyện trên split "train" của data/intent/examples.jsonl rồi đo: (1) split "test" của chính tệp (raw, không qua luật),
(2) hai bộ độc lập tests/fixtures/eval/heldout_intent.jsonl và rasa_coverage.jsonl qua parse_chat (cả luật). Với --holdout-only chỉ in hai bộ độc lập.
Mốc so sánh đo ngày 2026-11-09 trên cùng hai bộ: chỉ luật 106/138 và 185/212; Rasa + luật 130/138 và 209/212.
"""
from __future__ import annotations

import argparse
import asyncio
import collections
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from app.services.intent_classifier import IntentClassifier, load_examples  # noqa: E402
from app.services.transaction_parser import parse_chat  # noqa: E402

FIXTURES = ROOT / "tests" / "fixtures" / "eval"


def _cases(name: str, key: str) -> list[tuple[str, str]]:
    rows = [json.loads(line) for line in (FIXTURES / name).read_text(encoding="utf-8").splitlines() if line.strip()]
    return [(r["text"], r[key]) for r in rows if r[key] != "any"]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--c", type=float, default=20.0, help="độ mạnh điều chuẩn nghịch của hồi quy logistic")
    parser.add_argument("--holdout-only", action="store_true")
    parser.add_argument("--balanced", action="store_true", help="trọng số lớp cân bằng (bù lệch số câu giữa các ý định)")
    args = parser.parse_args()

    classifier = IntentClassifier.train(load_examples(splits=("train",)), c=args.c, class_weight="balanced" if args.balanced else None)
    if not args.holdout_only:
        test = load_examples(splits=("test",))
        raw = sum(classifier.predict(t)[0] == i for t, i in test)
        print(f"test split của tệp ví dụ: raw {raw}/{len(test)} ({raw / len(test):.1%})")

    async def run() -> None:
        for name, file, key in (("heldout", "heldout_intent.jsonl", "intent"), ("coverage", "rasa_coverage.jsonl", "expect")):
            cases, ok, wrong = _cases(file, key), 0, collections.Counter()
            for text, expected in cases:
                intent, confidence = classifier.predict(text)
                got = parse_chat(text, {"intent": intent, "confidence": confidence, "entities": {}}).intent
                if got == expected:
                    ok += 1
                else:
                    wrong[(expected, got)] += 1
            print(f"{name}: {ok}/{len(cases)} ({ok / len(cases):.1%}); sai nhiều nhất: {wrong.most_common(4)}")

    asyncio.run(run())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
