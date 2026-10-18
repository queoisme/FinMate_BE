"""Đo các bộ test NLU của Rasa qua TOÀN BỘ đường `POST /api/v1/chat/parse` (Rasa + luật), in một dòng JSON.

`rasa/tools/eval_live.py` đo Rasa thô; người dùng lại chỉ thấy kết quả SAU khi luật trong transaction_parser đã sửa bớt lỗi của Rasa. Ở đây "sai"
= `intent` trả về khác ý định mong đợi, và chia hai loại:
  - silent_wrong: sai mà KHÔNG có `unclear_reason` (không có gì nhắc người dùng xem lại);
  - auto_saved_wrong: sai mà có khoản `auto_save_ok` (sẽ bị ghi thẳng không hỏi). Phải bằng 0.

Cần file /tmp/nlu_cases.json (danh sách {set, intent, text}); `scripts/scorecard.py` tự chép vào. Chạy trong container ai-service:

    docker compose exec -T ai-service python - < ai-service/scripts/eval_nlu_pipeline.py
"""

import collections
import json
import os
import sys
import urllib.request
from pathlib import Path

URL = "http://localhost:8000/api/v1/chat/parse"
CASES = Path("/tmp/nlu_cases.json")


def call(text: str) -> dict:
    request = urllib.request.Request(
        URL,
        data=json.dumps({"text": text, "user_id_hash": "0" * 64, "allow_ai": False}).encode(),
        headers={"Content-Type": "application/json", "Authorization": f"Bearer {os.environ['INTERNAL_API_KEY']}"},
    )
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.loads(response.read())


if __name__ == "__main__":
    try:
        stats: dict[str, collections.Counter] = collections.defaultdict(collections.Counter)
        bad: dict[str, list[str]] = collections.defaultdict(list)
        for case in json.loads(CASES.read_text(encoding="utf-8")):
            out = call(case["text"])
            entries = out.get("entries") or ([out["entry"]] if out.get("entry") else [])
            tally = stats[case["set"]]
            tally["n"] += 1
            if out["intent"] == case["intent"]:
                tally["ok"] += 1
                continue
            silent = out.get("unclear_reason") is None
            auto = any(e.get("auto_save_ok") for e in entries)
            tally["silent_wrong"] += silent
            tally["auto_saved_wrong"] += auto
            bad[case["set"]].append(
                f"{case['text']!r}: muốn {case['intent']}, ra {out['intent']} (Rasa {out['nlu'].get('intent')} {out['nlu'].get('confidence')}"
                f"{', KHÔNG nhắc xem lại' if silent else ''}{', TỰ LƯU' if auto else ''})"
            )
        result = {name: dict(tally) | {"bad": bad[name]} for name, tally in stats.items()}
    except OSError as error:
        result = {"error": f"không chạy được: {error}"}
    sys.stdout.write(json.dumps(result, ensure_ascii=False))
