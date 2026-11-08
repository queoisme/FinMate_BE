"""Bộ chạy đánh giá AI Financial Agent: mỗi câu hỏi có nhãn chạy trên một hồ sơ giả lập, rồi chấm bằng ``agent_eval.metrics``.

    python -m agent_eval.run --config full

Cấu hình (cắt bỏ từng thành phần để đo đóng góp của nó):
  full           đủ GATE → PLAN → EXECUTE → COMPOSE → VERIFY
  no_verifier    bỏ bộ kiểm chứng số tiền (ai-service phải chạy với AGENT_EVAL_ENABLED=true)
  no_rag         tắt truy xuất tri thức (cờ có hiệu lực từ khi có RAG, Task 14)
  rasa_baseline  đường /chat/parse hiện có, không LLM: mốc so sánh
Nhà cung cấp/model LLM do ai-service chọn qua AGENT_PROVIDER/AGENT_MODEL: muốn so sánh thì chạy lại ai-service với biến khác rồi truyền ``--provider``/``--model`` chỉ để GHI NHÃN vào kết quả và tra bảng giá.

Cần backend chạy Development với AGENT_EVAL_ENABLED=true; khóa lấy từ biến môi trường AI_SERVICE_API_KEY (backend) và INTERNAL_API_KEY (ai-service), không bao giờ nhận qua dòng lệnh."""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import time
import uuid
from pathlib import Path
from typing import Any

import httpx

from agent_eval import metrics
from agent_eval.personas import make_persona

HERE = Path(__file__).resolve().parent
DATASET = HERE / "dataset" / "questions.jsonl"
RESULTS = HERE / "results"
PRICES = HERE / "prices.json"
CONFIGS = ("full", "no_verifier", "no_rag", "rasa_baseline")


def load_questions(limit: int | None = None) -> list[dict]:
    rows = [json.loads(line) for line in DATASET.read_text(encoding="utf-8").splitlines() if line.strip()]
    return rows[:limit] if limit else rows


def build_row(question: dict, payload: dict[str, Any], latency_ms: int, model: str) -> dict:
    """Gói phản hồi của ``/internal/agent/run`` (``data``) thành một hàng cho metrics."""
    answer = payload["answer"]
    return {
        "id": question["id"],
        "kind": question["kind"],
        "expect": question["expect"],
        "status": answer["status"],
        "tools": [s["tool"] for s in payload["trace"] if s["status"] == "Ok"],
        "claims": [{"text": c["text"], "evidence_ids": c["evidenceIds"]} for c in answer["claims"]],
        "evidence": [{"id": e["id"], "value_vnd": e["valueVnd"]} for e in answer["evidence"]],
        "latency_ms": latency_ms,
        "usage": {"input_tokens": payload["usage"]["inputTokens"], "output_tokens": payload["usage"]["outputTokens"], "model": model},
        "used_template": answer["usedTemplate"],
        "retrieved": [],
        "relevant": [],
    }


def summarize(rows: list[dict], price_table: dict) -> dict:
    p50, p95 = metrics.latency_p50_p95(rows)
    summary: dict[str, Any] = {
        "questions": len(rows),
        "plan_accuracy": metrics.plan_accuracy(rows),
        "tool_accuracy": metrics.tool_accuracy(rows),
        "unsupported_claim_rate": metrics.unsupported_claim_rate(rows),
        "numeric_faithfulness": metrics.numeric_faithfulness(rows),
        "refusal_precision": metrics.refusal_precision(rows),
        "refusal_recall": metrics.refusal_recall(rows),
        "retrieval_recall_at_5": metrics.retrieval_recall_at_k(rows, 5),
        "latency_p50_ms": p50,
        "latency_p95_ms": p95,
        "template_rate": sum(bool(r.get("used_template")) for r in rows) / len(rows) if rows else 0.0,
    }
    try:
        summary["cost_usd"] = sum(metrics.cost_usd(r["usage"], price_table) for r in rows)
    except KeyError:
        summary["cost_usd"] = None  # chưa có giá của model trong prices.json: báo thiếu, không in $0
    return summary


def markdown_table(config: str, summary: dict) -> str:
    lines = [f"| Chỉ số ({config}) | Giá trị |", "|---|---|"]
    for name, value in summary.items():
        lines.append(f"| {name} | {'n/a' if value is None else (round(value, 4) if isinstance(value, float) else value)} |")
    return "\n".join(lines)


def run_agent(client: httpx.Client, backend_url: str, key: str, question: dict, index: int, flags: dict, model: str) -> dict:
    persona = make_persona(question["segment"], index)
    body = {
        "snapshot": {
            "monthlyIncome": persona.snapshot["monthly_income"],
            "monthlySpend": persona.snapshot["monthly_spend"],
            "totalBalance": persona.snapshot["total_balance"],
        },
        "question": question["text"],
        "flags": flags,
    }
    started = time.monotonic()
    response = client.post(f"{backend_url}/api/v1/internal/agent/run", json=body, headers={"X-Eval-Key": key})
    latency = round((time.monotonic() - started) * 1000)
    response.raise_for_status()
    return build_row(question, response.json()["data"], latency, model)


def run_baseline(client: httpx.Client, ai_url: str, key: str, question: dict) -> dict:
    """Đường Rasa hiện có: không từ chối được, không công cụ, không bằng chứng. Hỏi lại khi nó tự đánh dấu câu chưa rõ."""
    started = time.monotonic()
    response = client.post(f"{ai_url}/api/v1/chat/parse", json={"text": question["text"]}, headers={"Authorization": f"Bearer {key}"})
    latency = round((time.monotonic() - started) * 1000)
    response.raise_for_status()
    unclear = response.json().get("unclear_reason") is not None
    return {
        "id": question["id"], "kind": question["kind"], "expect": question["expect"], "status": "clarify" if unclear else "answered", "tools": [], "claims": [], "evidence": [],
        "latency_ms": latency, "usage": {"input_tokens": 0, "output_tokens": 0, "model": "rasa"}, "used_template": False, "retrieved": [], "relevant": [],
    }


def run_questions(questions: list[dict], runner, path: Path) -> tuple[list[dict], list[str]]:
    """Chạy từng câu và ghi kết quả NGAY vào ``path`` (jsonl), nên một đợt dài bị ngắt giữa chừng không mất phần đã chạy.
    Câu nào gọi lỗi (mạng, 429, 503...) được ghi ``{"id", "error"}`` rồi chạy tiếp; chạy lại cùng ``path`` thì bỏ qua câu đã trả lời và thử lại câu lỗi.
    Trả (các hàng đã trả lời, id các câu còn lỗi): câu lỗi KHÔNG vào chỉ số, cũng không bị tính là đúng."""
    latest: dict[str, dict] = {}
    if path.exists():
        for line in path.read_text(encoding="utf-8").splitlines():
            if line.strip():
                row = json.loads(line)
                latest[row["id"]] = row  # dòng cuối của mỗi id thắng
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as out:
        for index, question in enumerate(questions):  # index theo vị trí gốc: hồ sơ giả lập không đổi khi resume
            done = latest.get(question["id"])
            if done is not None and "error" not in done:
                continue
            try:
                row = runner(question, index)
            except (httpx.HTTPError, KeyError, ValueError) as error:
                row = {"id": question["id"], "error": type(error).__name__}  # không ghi nội dung lỗi: có thể chứa câu hỏi
            latest[question["id"]] = row
            out.write(json.dumps(row, ensure_ascii=False) + "\n")
            out.flush()
    ordered = [latest[q["id"]] for q in questions if q["id"] in latest]
    return [r for r in ordered if "error" not in r], [r["id"] for r in ordered if "error" in r]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="agent_eval.run", description="Chạy bộ câu hỏi có nhãn qua agent và chấm điểm.")
    parser.add_argument("--config", choices=CONFIGS, default="full")
    parser.add_argument("--backend-url", default=os.environ.get("FINMATE_BACKEND_URL", "http://localhost:8080"))
    parser.add_argument("--ai-url", default=os.environ.get("FINMATE_AI_URL", "http://localhost:8000"))
    parser.add_argument("--limit", type=int, default=None, help="chỉ chạy N câu đầu (chạy thử)")
    parser.add_argument("--provider", default=os.environ.get("AGENT_PROVIDER", "unknown"), help="nhãn ghi vào kết quả")
    parser.add_argument("--model", default=os.environ.get("AGENT_MODEL", "unknown"), help="nhãn ghi vào kết quả và khóa tra bảng giá")
    parser.add_argument("--resume", default=None, help="mã đợt chạy cũ (tên tệp trong agent_eval/results/ không có đuôi): bỏ qua câu đã trả lời, thử lại câu lỗi")
    args = parser.parse_args(argv)
    if args.resume is not None and not re.fullmatch(r"[0-9]{8}-[0-9]{6}-[a-z_]+-[0-9a-f]{6}", args.resume):
        parser.error("--resume phải là mã đợt chạy dạng 20261108-101500-full-ab12cd")

    price_table = json.loads(PRICES.read_text(encoding="utf-8")) if PRICES.exists() else {}
    flags = {"verifier": args.config != "no_verifier", "rag": args.config != "no_rag"}
    run_id = args.resume or f"{time.strftime('%Y%m%d-%H%M%S')}-{args.config}-{uuid.uuid4().hex[:6]}"
    with httpx.Client(timeout=90) as client:
        if args.config == "rasa_baseline":
            def runner(question: dict, index: int) -> dict:
                return run_baseline(client, args.ai_url, os.environ["INTERNAL_API_KEY"], question)
        else:
            def runner(question: dict, index: int) -> dict:
                return run_agent(client, args.backend_url, os.environ["AI_SERVICE_API_KEY"], question, index, flags, args.model)

        rows, errored = run_questions(load_questions(args.limit), runner, RESULTS / f"{run_id}.jsonl")

    if not rows:
        print(f"Không câu nào trả lời được ({len(errored)} lỗi). Kiểm tra backend / ai-service rồi chạy lại với --resume {run_id}")
        return 1
    summary = summarize(rows, price_table)
    summary["errored"] = len(errored)
    RESULTS.mkdir(exist_ok=True)
    (RESULTS / f"{run_id}.json").write_text(
        json.dumps({"run_id": run_id, "config": args.config, "provider": args.provider, "model": args.model, "summary": summary, "rows": rows, "errored_ids": errored}, ensure_ascii=False, indent=1),
        encoding="utf-8",
    )
    table = markdown_table(args.config, summary)
    (RESULTS / f"{run_id}.md").write_text(table + "\n", encoding="utf-8")
    print(table)
    print(f"\nKết quả: agent_eval/results/{run_id}.json")
    return 0


if __name__ == "__main__":
    sys.exit(main())
