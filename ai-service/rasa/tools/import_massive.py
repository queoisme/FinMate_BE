#!/usr/bin/env python3
"""Lấy câu tiếng Việt KHÔNG liên quan chi tiêu từ MASSIVE 1.1 (vi-VN) làm ví dụ `out_of_scope` cho Rasa.

MASSIVE: Amazon Science, giấy phép CC BY 4.0 (https://github.com/alexa/massive). Dữ liệu được ĐIỀU CHỈNH ở đây: chọn một số chủ đề, loại câu nhắc tới
tiền/mua bán, đổi nhãn ý định thành `out_of_scope`, và giữ nhãn thực thể ngày (`[date : x]` -> `[x](date)`). Yêu cầu ghi nguồn: giữ phần chú thích đầu tệp.

Ra hai tệp:
  data/nlu_massive_oos.yml          huấn luyện (từ phần train + dev của MASSIVE)
  tests/nlu_test_massive_oos.yml    đo (từ phần test của MASSIVE; Rasa không được thấy khi học)

Chạy:  python rasa/tools/import_massive.py <đường dẫn vi-VN.jsonl>
"""
from __future__ import annotations

import json
import pathlib
import random
import re
import sys
import unicodedata

RASA = pathlib.Path(__file__).resolve().parents[1]
SEED = 20261012
# Chỉ lấy chủ đề rõ ràng không phải chi tiêu. Bỏ takeaway (đặt đồ ăn), transport (gọi xe), recommendation (gợi ý quán), qa (hỏi đáp có thể về giá).
SCENARIOS = {"weather", "music", "play", "news", "iot", "email", "social", "audio", "alarm", "cooking", "datetime", "calendar", "lists"}
# Bỏ mọi câu có dấu hiệu tiền bạc/mua bán dù nằm trong chủ đề an toàn (so trên chữ đã bỏ dấu).
MONEY_WORDS = re.compile(r"\b(tien|gia|dong|vnd|nghin|ngan|trieu|ty|mua|ban|chi|tieu|tra|luong|thuong|ngan hang|the|vi|chuyen|rut|nap|hoa don|thanh toan|do la|usd|k)\b|\d\s?k\b")
PER_SCENARIO = {"train": 28, "test": 10}


def fold(text: str) -> str:
    text = unicodedata.normalize("NFD", text.replace("đ", "d").replace("Đ", "D"))
    return "".join(c for c in text if unicodedata.category(c) != "Mn").lower()


def to_rasa(annot: str) -> str | None:
    """`[date : thứ sáu]` -> `[thứ sáu](date)`; mọi thực thể khác bỏ nhãn (chỉ giữ chữ). Câu còn ký tự lạ thì bỏ."""
    def repl(match: re.Match) -> str:
        slot, value = match.group(1).strip(), match.group(2).strip()
        return f"[{value}](date)" if slot == "date" else value

    text = re.sub(r"\[([a-z_]+) : ([^\]]+)\]", repl, annot)
    return None if "[" in re.sub(r"\[[^\]]+\]\(date\)", "", text) or "]" in re.sub(r"\[[^\]]+\]\(date\)", "", text) else " ".join(text.split())


def main() -> int:
    source = pathlib.Path(sys.argv[1])
    rows = [json.loads(line) for line in source.read_text(encoding="utf-8").splitlines() if line.strip()]
    rng = random.Random(SEED)
    out: dict[str, list[str]] = {"train": [], "test": []}
    for split, partitions in (("train", {"train", "dev"}), ("test", {"test"})):
        by_scenario: dict[str, list[str]] = {}
        for row in rows:
            if row["partition"] not in partitions or row["scenario"] not in SCENARIOS or MONEY_WORDS.search(fold(row["utt"])):
                continue
            example = to_rasa(row["annot_utt"])
            if example and 2 <= len(row["utt"].split()) <= 14:
                by_scenario.setdefault(row["scenario"], []).append(example)
        for scenario in sorted(by_scenario):
            pool = sorted(set(by_scenario[scenario]))
            rng.shuffle(pool)
            out[split] += pool[: PER_SCENARIO[split]]
    train_plain = {re.sub(r"\[([^\]]+)\]\(date\)", r"\1", e) for e in out["train"]}
    out["test"] = [e for e in out["test"] if re.sub(r"\[([^\]]+)\]\(date\)", r"\1", e) not in train_plain]  # không để câu test trùng câu học

    header = ("# NGUỒN: MASSIVE 1.1 (Amazon Science), giấy phép CC BY 4.0, https://github.com/alexa/massive. Đã điều chỉnh: chọn chủ đề không liên quan chi tiêu,\n"
              "# đổi nhãn ý định thành out_of_scope, giữ thực thể ngày. Sinh bởi rasa/tools/import_massive.py (seed cố định).")
    for name, path, examples in (("train", RASA / "data" / "nlu_massive_oos.yml", out["train"]), ("test", RASA / "tests" / "nlu_test_massive_oos.yml", out["test"])):
        lines = ['version: "3.1"', "", header, "nlu:", "- intent: out_of_scope", "  examples: |"] + [f"    - {e}" for e in sorted(set(examples))] + [""]
        path.write_text("\n".join(lines), encoding="utf-8")
        print(name, len(set(examples)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
