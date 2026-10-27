"""Chạy bộ câu chat cố định qua endpoint ĐANG CHẠY `POST /api/v1/chat/parse` (có Rasa thật), in một dòng JSON.

Khác `eval_flows.py` (gọi luật ngay trong tiến trình, không Rasa, không Gemini): đây là đường mà backend gọi thật, nên có độ trễ và
tỷ lệ Rasa trả lời. Mặc định tắt AI (`allow_ai=false`) để số đo lặp lại được và không tốn hạn mức; thêm `--ai` để đo cả Gemini thật
(tốn một lượt gọi cho mỗi câu có sản phẩm, và gặp 429 thì chat tự rơi về luật, nên kết quả phụ thuộc hạn mức lúc đó).

Chạy trong container ai-service (không import app, chỉ cần urllib):

    docker compose exec -T ai-service python - [--ai] < ai-service/scripts/eval_chat_live.py
"""

import json
import os
import statistics
import sys
import time
import urllib.request
from pathlib import Path

URL = os.environ.get("CHAT_PARSE_URL", "http://localhost:8000/api/v1/chat/parse")  # đổi khi đo một ai-service thử (model Rasa khác) trên cổng khác
CASES = Path("/src/tests/fixtures/eval/chat_cases.jsonl")
USER_HASH = "0" * 64


def call(text: str, allow_ai: bool) -> tuple[dict, float]:
    request = urllib.request.Request(
        URL,
        data=json.dumps({"text": text, "user_id_hash": USER_HASH, "allow_ai": allow_ai}).encode(),
        headers={"Content-Type": "application/json", "Authorization": f"Bearer {os.environ['INTERNAL_API_KEY']}"},
    )
    started = time.perf_counter()
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.loads(response.read()), (time.perf_counter() - started) * 1000


def measure(allow_ai: bool) -> dict:
    n = correct = wrong_confident = abstained_n = rasa_n = 0
    failures, latencies = [], []
    for line in CASES.read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        case = json.loads(line)
        out, ms = call(case["text"], allow_ai)
        latencies.append(ms)
        rasa_n += out["nlu"]["source"] == "rasa"
        if "intent" in case:
            want, got, abstained = case["intent"], out["intent"], False
        else:
            want = [(e["kind"], e["amount"], e["category"]) for e in case["expect"]]
            entries = out["entries"] or ([out["entry"]] if out["entry"] else [])
            got = [(e["kind"], e["amount_cents"], e["category_slug"]) for e in entries]
            abstained = any(k == "expense" and c is None for k, _, c in got)
        n += 1
        abstained_n += abstained
        if want == got:
            correct += 1
        else:
            wrong_confident += not abstained
            failures.append(f"{case['text']!r}: muốn {want}, ra {got}")
    latencies.sort()
    return {
        "n": n,
        "accuracy": round(correct / n, 3),
        "wrong_confident": wrong_confident,
        "abstain_rate": round(abstained_n / n, 3),
        "rasa_rate": round(rasa_n / n, 3),
        "p50_ms": round(statistics.median(latencies)),
        "p95_ms": round(latencies[int(len(latencies) * 0.95) - 1]),
        "failures": failures[:15],
    }


if __name__ == "__main__":
    try:
        result = {"rules_rasa": measure(False)}
        if "--ai" in sys.argv:
            result["with_ai"] = measure(True)
    except OSError as error:
        result = {"error": f"không gọi được ai-service: {error}"}
    sys.stdout.write(json.dumps(result, ensure_ascii=False))
