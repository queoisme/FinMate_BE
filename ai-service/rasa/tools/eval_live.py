"""Đo Rasa ĐANG CHẠY (model đang nạp) trên các bộ tests/*.yml, in một dòng JSON.

Không cần cài gì thêm; chạy trong container rasa (đã có yaml, server ở localhost:5005, thư mục tests mount ở /app/tests):

    docker compose exec -T rasa python - < ai-service/rasa/tools/eval_live.py

Khác `rasa test nlu`: đo đúng service ai-service gọi, kèm độ trễ và "sai mà tự tin" (đoán sai với độ chắc >= 0,8).
"""

import json
import os
import re
import statistics
import sys
import time
import urllib.request
from pathlib import Path

import yaml

URL = os.environ.get("RASA_PARSE_URL", "http://localhost:5005/model/parse")  # đổi khi đo một model thử chạy trên cổng khác
TESTS = Path("/app/tests")
CONFIDENT = 0.8
ENTITY = re.compile(r"\[([^\]]+)\]\((\w+)\)")


def cases(path: Path):
    for block in yaml.safe_load(path.read_text(encoding="utf-8"))["nlu"]:
        for line in block["examples"].splitlines():
            line = line.strip().removeprefix("- ").strip()
            if line:
                amounts = [m.group(1) for m in ENTITY.finditer(line) if m.group(2) == "amount"]
                yield block["intent"], ENTITY.sub(r"\1", line), amounts


def parse(text: str) -> tuple[dict, float]:
    request = urllib.request.Request(URL, data=json.dumps({"text": text}).encode(), headers={"Content-Type": "application/json"})
    started = time.perf_counter()
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.loads(response.read()), (time.perf_counter() - started) * 1000


def measure(path: Path) -> dict:
    n = correct = wrong_confident = wrong_saturated = amount_want = amount_hit = amount_extra = 0
    wrong, latencies = [], []
    for intent, text, amounts in cases(path):
        out, ms = parse(text)
        latencies.append(ms)
        got, confidence = out["intent"]["name"], out["intent"]["confidence"]
        n += 1
        correct += got == intent
        if got != intent:
            wrong_confident += confidence >= CONFIDENT
            wrong_saturated += confidence >= 0.97  # ngưỡng KIND_TRUST của transaction_parser: trên mức này Rasa thắng luật
            wrong.append(f"{text!r}: muốn {intent}, ra {got} ({confidence:.2f})")
        found = [e["value"] for e in out["entities"] if e["entity"] == "amount"]
        amount_want += len(amounts)
        amount_hit += sum(1 for a in amounts if a in found)
        amount_extra += sum(1 for v in found if v not in amounts)
    latencies.sort()
    return {
        "n": n,
        "intent_accuracy": round(correct / n, 3),
        "wrong_confident": wrong_confident,
        "wrong_saturated": wrong_saturated,
        "amount_recall": round(amount_hit / amount_want, 3) if amount_want else None,
        "amount_spurious": amount_extra,
        "p50_ms": round(statistics.median(latencies)),
        "p95_ms": round(latencies[int(len(latencies) * 0.95) - 1]),
        "wrong": wrong[:15],
    }


if __name__ == "__main__":
    try:
        result = {path.stem: measure(path) for path in sorted(TESTS.glob("nlu_test*.yml"))}
    except OSError as error:
        result = {"error": f"không gọi được Rasa: {error}"}
    sys.stdout.write(json.dumps(result, ensure_ascii=False))
