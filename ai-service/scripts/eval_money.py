#!/usr/bin/env python3
"""Một bộ câu đọc tiền tiếng Việt chạy qua các bộ đọc số tiền của AI service.

    chat      = app.services.amount_normalizer.parse_amount   (bộ đọc của chat)
    chat_full = parse_chat(...) cả luồng: tách khoản, chọn số, loại giao dịch. Voice đi đường này:
                audio -> stt-service -> backend VoiceChatCommandHandler -> /api/v1/chat/parse -> parse_chat
    generic   = app.utils.text_utils.pick_amount              (thông báo ngân hàng, /transactions/parse; KHÔNG phải đường của voice)

Số đo này chỉ phủ phần CHỮ -> số tiền. Phần audio -> chữ (STT) không nằm trong đây.
Nhãn kỳ vọng viết theo cách người Việt nói, không theo kết quả hiện tại. Nhóm "ambiguous" (amount = null)
không chấm; chỉ in ra các bên đọc gì và cảnh báo khi ra số < 1.000đ.

money_cases.jsonl đã được dùng để chỉnh code nên số đo trên đó lạc quan; money_holdout.jsonl viết trước khi chạy và
không được dùng để chỉnh code. Hãy báo số của holdout là số đại diện.

Chạy:  python scripts/eval_money.py [--fails-only] [money_cases.jsonl money_holdout.jsonl ...]
"""

from __future__ import annotations

import argparse
import json
import pathlib
import sys
from collections import defaultdict
from datetime import datetime

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from app.services.amount_normalizer import parse_amount  # noqa: E402
from app.services.transaction_parser import VN, parse_chat  # noqa: E402
from app.utils.text_utils import pick_amount  # noqa: E402

FIXTURES = pathlib.Path(__file__).resolve().parent.parent / "tests" / "fixtures" / "eval"
NOW = datetime(2026, 10, 12, 10, 0, tzinfo=VN)


def chat(text: str) -> int | None:
    match = parse_amount(text)
    return match.value if match else None


def chat_full(text: str) -> int | None:
    parsed = parse_chat(text, None, now=NOW)
    entries = list(getattr(parsed, "entries", None) or ([parsed.entry] if parsed.entry else []))
    return entries[0].amount if len(entries) == 1 else None  # mỗi câu test chỉ có một khoản


def generic(text: str) -> int | None:
    candidate = pick_amount(text)
    return candidate.value if candidate else None


READERS = {"chat": chat, "chat_full": chat_full, "generic": generic}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("files", nargs="*", default=["money_cases.jsonl"])
    parser.add_argument("--fails-only", action="store_true", help="chỉ in các ca sai")
    args = parser.parse_args()

    cases = [
        json.loads(line)
        for name in args.files
        for line in (FIXTURES / name).read_text(encoding="utf-8").splitlines()
        if line.strip()
    ]
    scored = [c for c in cases if c["amount"] is not None]
    ambiguous = [c for c in cases if c["amount"] is None]

    by_group: dict[str, dict[str, list[int]]] = defaultdict(lambda: {name: [0, 0] for name in READERS})
    rows = []
    for case in scored:
        got = {name: read(case["text"]) for name, read in READERS.items()}
        for name, value in got.items():
            tally = by_group[case["group"]][name]
            tally[0] += value == case["amount"]
            tally[1] += 1
        rows.append((case, got))

    print(f"{', '.join(args.files)}: {len(scored)} ca có đáp án + {len(ambiguous)} ca chưa có đáp án chung\n")
    print(f"{'nhóm':<14}" + "".join(f"{name:>11}" for name in READERS))
    totals = {name: [0, 0] for name in READERS}
    for group, tallies in by_group.items():
        cells = []
        for name in READERS:
            ok, n = tallies[name]
            totals[name][0] += ok
            totals[name][1] += n
            cells.append(f"{ok}/{n}")
        print(f"{group:<14}" + "".join(f"{cell:>11}" for cell in cells))
    print(f"{'TỔNG':<14}" + "".join(f"{totals[n][0] / totals[n][1]:>11.0%}" for n in READERS))
    print(f"{'':<14}" + "".join(f"{totals[n][0]}/{totals[n][1]}".rjust(11) for n in READERS))

    print("\nCA SAI (✓/✗ theo thứ tự: " + " | ".join(READERS) + ")")
    for case, got in rows:
        if all(v == case["amount"] for v in got.values()):
            continue
        marks = "".join("✓ " if got[n] == case["amount"] else "✗ " for n in READERS)
        values = " ".join(f"{n}={got[n]}" for n in READERS)
        print(f"  {marks}[{case['group']}] {case['text']!r}: want={case['amount']} {values}")

    if not args.fails_only:
        print("\nCA CHƯA CÓ ĐÁP ÁN CHUNG (⚠ = ra số < 1.000đ, gần như chắc là thiếu 'nghìn')")
        for case in ambiguous:
            got = {name: read(case["text"]) for name, read in READERS.items()}
            warn = "⚠ " if any(v is not None and v < 1000 for v in got.values()) else "  "
            values = " ".join(f"{n}={got[n]}" for n in READERS)
            print(f"  {warn}{case['text']!r}: {values}  ({case.get('note', '')})")


if __name__ == "__main__":
    main()
