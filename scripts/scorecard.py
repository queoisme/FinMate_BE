#!/usr/bin/env python3
"""Bảng điểm khả thi của chatbot (Rasa + luật + AI), Speech (STT) và OCR, trong một lần chạy.

Chạy từ gốc repo, khi `docker compose up` đã chạy các service:

    python scripts/scorecard.py                  # đo tất cả (~4 phút, phần lớn là OCR), ghi docs/scorecard.md
    python scripts/scorecard.py --skip ocr       # bỏ OCR (nhanh, ~30 giây)
    python scripts/scorecard.py --only rasa,stt
    python scripts/scorecard.py --ai             # đo thêm chat có Gemini thật (tốn hạn mức, kết quả phụ thuộc hạn mức lúc đó)

Chỉ dùng thư viện chuẩn: mỗi phép đo chạy TRONG container của service đó (đúng model, đúng cấu hình đang chạy) và script này chỉ đọc kết quả.
Ngưỡng ở mỗi `row(...)` là ĐỀ XUẤT để chủ dự án sửa; số đo nhỏ và do chính người viết luật gán nhãn, nên đọc cùng phần "Độ tin cậy".
"""

import argparse
import ast
import json
import os
import re
import subprocess
import sys
import tempfile
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "docs" / "scorecard.md"
DATA_BILL = ROOT.parent / "ai-service" / "data-bill"  # ảnh bill thật, nằm ngoài git
VOICE = ROOT / "stt-service" / "Data_Voice"


# ---------------------------------------------------------------- chạy lệnh trong container

def compose(*args: str, stdin: Path | None = None, timeout: int = 900) -> str:
    env = dict(os.environ, MSYS_NO_PATHCONV="1", PYTHONIOENCODING="utf-8")
    done = subprocess.run(["docker", "compose", *args], cwd=ROOT, env=env, capture_output=True, timeout=timeout,
                          input=stdin.read_bytes() if stdin else b"")
    out = done.stdout.decode("utf-8", "replace")
    if done.returncode:
        raise RuntimeError((done.stderr.decode("utf-8", "replace") or out).strip()[-300:])
    return out


def run_in(service: str, *cmd: str, env: dict[str, str] | None = None, stdin: Path | None = None, timeout: int = 900) -> str:
    flags = [x for k, v in (env or {}).items() for x in ("-e", f"{k}={v}")]
    return compose("exec", "-T", *flags, service, *cmd, stdin=stdin, timeout=timeout)


def wipe(service: str, *paths: str) -> None:
    """Xóa thư mục đã chép vào container; chạy bằng root vì `docker cp` tạo file thuộc root."""
    compose("exec", "-T", "-u", "root", service, "rm", "-rf", *paths)


def json_from(text: str) -> dict:
    data = json.loads(text)
    if "error" in data:
        raise RuntimeError(data["error"])
    return data


def number(pattern: str, text: str, group: int = 1) -> float:
    match = re.search(pattern, text, re.M)
    if not match:
        raise RuntimeError(f"không đọc được kết quả (mẫu: {pattern[:40]})")
    return float(match.group(group))


def ratio(pattern: str, text: str) -> float:
    match = re.search(pattern, text, re.M)
    if not match:
        raise RuntimeError(f"không đọc được kết quả (mẫu: {pattern[:40]})")
    return int(match.group(1)) / int(match.group(2))


# ---------------------------------------------------------------- hàng kết quả

def row(label: str, value: float | None, target: float | None = None, *, higher: bool = True, fmt: str = "{:.1%}") -> dict:
    if value is None or target is None:
        status = "—"
    else:
        status = "ĐẠT" if (value >= target if higher else value <= target) else "CHƯA ĐẠT"
    goal = "" if target is None else f"{'≥' if higher else '≤'} {fmt.format(target)}"
    return {"label": label, "value": "n/a" if value is None else fmt.format(value), "goal": goal, "status": status}


MS = "{:.0f} ms"


# ---------------------------------------------------------------- các phép đo (mỗi cái trả (rows, chi tiết))

def chat_rules():
    out = run_in("ai-service", "python", "scripts/eval_flows.py")
    chat = re.search(r"== chat: (\d+)/(\d+) đúng, accuracy ([\d.]+), bỏ qua ([\d.]+), sai mà tự tin (\d+)", out)
    items = re.search(r"== items: (\d+)/(\d+) đúng, accuracy ([\d.]+), bỏ qua ([\d.]+), sai mà tự tin (\d+)", out)
    if not (chat and items):
        raise RuntimeError("không đọc được kết quả eval_flows")
    rows = [
        row(f"Chat: đúng ({chat[2]} câu)", float(chat[3]), 0.95),
        row("Chat: sai mà vẫn tự tin", int(chat[5]) / int(chat[2]), 0.02, higher=False),
        row(f"Món trên bill: sai mà vẫn tự tin ({items[2]} món, chưa có AI)", int(items[5]) / int(items[2]), 0.05, higher=False),
        row("Món trên bill: đúng (chỉ luật, phần còn lại cần AI)", float(items[3])),
        row("Chat và bill ra cùng danh mục cho cùng một cụm", number(r"agree_rate ([\d.]+)", out), 0.90),
    ]
    return rows, [line.strip().removeprefix("- ") for line in re.findall(r"^   - .*$", out, re.M)[:8]]


def money():
    rows = []
    for label, file in (("bộ chính", "money_cases.jsonl"), ("bộ giữ riêng", "money_holdout.jsonl")):
        out = run_in("ai-service", "python", "scripts/eval_money.py", file)
        counts = re.search(r"^TỔNG.*\n\s+\d+/\d+\s+(\d+)/(\d+)", out, re.M)
        if not counts:
            raise RuntimeError("không đọc được kết quả eval_money")
        rows.append(row(f"Đọc số tiền trong câu chat/voice, {label} ({counts[2]} câu)", int(counts[1]) / int(counts[2]), 0.95))
    return rows, []


def chat_live(with_ai: bool):
    d = json_from(run_in("ai-service", "python", "-", *(["--ai"] if with_ai else []),
                         stdin=ROOT / "ai-service/scripts/eval_chat_live.py"))
    rows, details = [], []
    for key, label in (("rules_rasa", "Rasa + luật"), ("with_ai", "Rasa + luật + Gemini")):
        if key not in d:
            continue
        m = d[key]
        rows += [
            row(f"{label}: đúng ({m['n']} câu)", m["accuracy"], 0.95),
            row(f"{label}: sai mà vẫn tự tin", m["wrong_confident"] / m["n"], 0.02, higher=False),
            row(f"{label}: Rasa có trả lời", m["rasa_rate"], 0.95),
            row(f"{label}: thời gian p95", m["p95_ms"], 9000 if key == "with_ai" else 500, higher=False, fmt=MS),
        ]
        details += m["failures"][:8]
    return rows, details


RASA_SETS = {  # tên file -> (nhãn, ngưỡng độ chính xác ý định)
    "nlu_test": ("câu viết tay", 0.90),
    "nlu_test_fresh": ("viết tay, món chưa từng thấy", 0.85),
    "nlu_test_fresh2": ("viết tay, khung câu chưa từng thấy", 0.85),
    "nlu_test_massive_oos": ("ngoài chủ đề (MASSIVE)", 0.90),
    "nlu_test_v2": ("tổng hợp, nhiều biến thể", 0.80),
}


def _nlu_cases() -> list[dict]:
    """Đọc ai-service/rasa/tests/nlu_test*.yml (định dạng cố định, không cần PyYAML): [{set, intent, text}] với chú thích thực thể đã bỏ."""
    cases = []
    for path in sorted((ROOT / "ai-service/rasa/tests").glob("nlu_test*.yml")):
        intent = None
        for line in path.read_text(encoding="utf-8").splitlines():
            if line.startswith("- intent:"):
                intent = line.split(":", 1)[1].strip()
            elif intent and line.startswith("    - "):
                cases.append({"set": path.stem, "intent": intent, "text": re.sub(r"\[([^\]]+)\]\(\w+\)", r"\1", line[6:].strip())})
    return cases


def rasa():
    d = json_from(run_in("rasa", "python", "-", stdin=ROOT / "ai-service/rasa/tools/eval_live.py"))
    rows, details = [], []
    for stem, (label, target) in RASA_SETS.items():
        if stem not in d:
            continue
        m = d[stem]
        rows += [
            row(f"Rasa thô, ý định, {label} ({m['n']} câu)", m["intent_accuracy"], target),
            # Chỉ để tham khảo: luật trong parser chặn phần lớn các ca này; ngưỡng đặt ở chỉ tiêu "sau luật" bên dưới.
            row(f"Rasa thô, đoán sai mà độ chắc ≥ 0,8, {label}", m["wrong_confident"] / m["n"]),
        ]
        if m["amount_recall"] is not None:
            rows.append(row(f"Rasa thô, cắt đúng số tiền, {label}", m["amount_recall"], 0.90))
        details += [f"[{label}] {w}" for w in m["wrong"][:3]]
    rows.append(row("Thời gian p95 một câu", max(m["p95_ms"] for m in d.values()), 200, higher=False, fmt=MS))

    # Tầng sản phẩm: cùng các câu đó đi qua /chat/parse (Rasa + luật). Đây mới là thứ người dùng thấy.
    with tempfile.TemporaryDirectory() as tmp:
        cases = Path(tmp) / "nlu_cases.json"
        cases.write_text(json.dumps(_nlu_cases(), ensure_ascii=False), encoding="utf-8")
        compose("cp", str(cases), "ai-service:/tmp/nlu_cases.json")
    p = json_from(run_in("ai-service", "python", "-", stdin=ROOT / "ai-service/scripts/eval_nlu_pipeline.py"))
    for stem, (label, _) in RASA_SETS.items():
        if stem in p:
            rows.append(row(f"Sau luật, đúng ý định, {label} ({p[stem]['n']} câu)", p[stem].get("ok", 0) / p[stem]["n"], 0.90))
    total = sum(m["n"] for m in p.values())
    rows += [
        row(f"Sau luật, sai mà KHÔNG nhắc xem lại (cả {total} câu)", sum(m.get("silent_wrong", 0) for m in p.values()) / total, 0.03, higher=False),
        row("Sau luật, sai mà bị TỰ LƯU không hỏi", sum(m.get("auto_saved_wrong", 0) for m in p.values()), 0, higher=False, fmt="{:.0f}"),
    ]
    details += [f"[sau luật] {w}" for m in p.values() for w in m["bad"] if "KHÔNG nhắc" in w or "TỰ LƯU" in w][:10]
    return rows, details


def _voice_failure(line: str) -> str:
    try:
        text, want_amt, got_amt, want_type, got_type, want_cat, got_cat = ast.literal_eval(line.strip())
    except (ValueError, SyntaxError):
        return line.strip()
    return f"{text!r}: tiền {want_amt}→{got_amt}, loại {want_type}→{got_type}, nhóm {want_cat}→{got_cat}"


def voice_text():
    out = run_in("ai-service", "python", "scripts/eval_voice_text.py", env={"PYTHONPATH": "/src"})
    n = int(number(r"cases scored: (\d+)", out))
    rows = [
        row(f"Chữ sau STT → số tiền đúng ({n} câu)", ratio(r"amount exact : (\d+)/(\d+)", out), 0.95),
        row("Chữ sau STT → thu/chi đúng", ratio(r"type correct : (\d+)/(\d+)", out), 0.95),
        row("Chữ sau STT → danh mục đúng", ratio(r"category\s+: (\d+)/(\d+)", out), 0.85),
    ]
    return rows, [_voice_failure(line) for line in re.findall(r"^  \('.*$", out, re.M)[:6]]


def stt():
    if not VOICE.is_dir():
        raise RuntimeError(f"không có thư mục ghi âm {VOICE}")
    wipe("stt-service", "/tmp/voice")
    compose("cp", str(VOICE.relative_to(ROOT)), "stt-service:/tmp/voice")
    try:
        d = json_from(run_in("stt-service", "python", "-", stdin=ROOT / "stt-service/tools/eval_wer.py"))
    finally:
        wipe("stt-service", "/tmp/voice")
    label = "phần đầu câu" if d["prefix_only"] else "cả câu"
    rows = [
        row(f"Tỷ lệ lỗi từ (WER), {label} ({d['n']} file)", d["wer"], 0.10, higher=False),
        row("Câu nhận dạng đúng hoàn toàn", d["exact_rate"]),
        row("Tốc độ: thời gian xử lý / thời lượng âm thanh", d["rtf"], 0.5, higher=False, fmt="{:.2f}"),
    ]
    return rows, [f"WER {c['wer']:.0%}: muốn {c['ref']!r} / ra {c['hyp']!r}" for c in d["worst"] if c["wer"] > 0]


def ocr():
    if not DATA_BILL.is_dir():
        raise RuntimeError(f"không có thư mục ảnh bill {DATA_BILL}")
    wipe("ocr-service", "/app/benchmark", "/tmp/data-bill")
    compose("cp", "ocr-service/benchmark", "ocr-service:/app/benchmark")
    compose("cp", str(DATA_BILL), "ocr-service:/tmp/data-bill")
    try:
        out = run_in("ocr-service", "python", "benchmark/run_databill.py", env={"DATA_BILL": "/tmp/data-bill"}, timeout=1500)
    finally:
        wipe("ocr-service", "/app/benchmark", "/tmp/data-bill")
    n = int(number(r"=== (\d+) ảnh thật", out))
    rows = [
        row(f"Tổng tiền đúng ({n} ảnh)", ratio(r"tổng tiền đúng\s*: (\d+)/(\d+)", out), 0.95),
        row("LỖI ÂM THẦM: sai mà app không nhắc kiểm tra", ratio(r"LỖI ÂM THẦM\s*: (\d+)/(\d+)", out), 0.02, higher=False),
        row("Đúng cả tổng, tên cửa hàng, danh mục", ratio(r"đúng cả ba\s*: (\d+)/(\d+)", out), 0.85),
        row("Tên cửa hàng đúng", ratio(r"tên cửa hàng đúng\s*: (\d+)/(\d+)", out), 0.90),
        row("Danh mục đúng", ratio(r"danh mục đúng\s*: (\d+)/(\d+)", out), 0.90),
        row("Bill bị nhắc kiểm tra (phiền, không hại)", ratio(r"nhắc kiểm tra\s*: (\d+)/(\d+)", out)),
        row("Thời gian trung bình một ảnh", number(r"thời gian TB\s*: ([\d.]+)", out), 5, higher=False, fmt="{:.1f} giây"),
    ]
    return rows, []


# ---------------------------------------------------------------- khối, nhóm, độ tin cậy

BLOCKS = {  # khóa -> (nhóm, tiêu đề, hàm, độ tin cậy)
    "chat_rules": ("Chatbot", "Chat và món: luật + từ điển (không Rasa, không AI)", chat_rules,
                   "Bộ câu do người sửa luật viết nhãn, nhỏ (~66 câu chat, ~127 món). Cho thấy hướng đi, không phải độ chính xác với người dùng thật."),
    "money": ("Chatbot", "Đọc số tiền tiếng Việt (chữ, 'k', 'củ', 'lít')", money,
              "Bộ giữ riêng ít bị tinh chỉnh theo hơn bộ chính, nhưng cũng do người viết luật gán nhãn."),
    "chat_live": ("Chatbot", "Chat qua endpoint thật (Rasa đang chạy)", None,
                  "Cùng bộ câu với khối luật, nhưng đi đúng đường backend gọi. Khác biệt với khối luật chính là phần Rasa gây ra."),
    "rasa": ("Chatbot", "Rasa NLU (ý định + số tiền)", rasa,
             "Hai bộ viết tay 'món chưa từng thấy' (nlu_test_fresh) và 'khung câu chưa từng thấy' (nlu_test_fresh2) là bộ độc lập nhất: không được đưa vào data/ (generate_nlu.py chặn). "
             "Bộ 'tổng hợp' do máy sinh, món và mẫu câu tách khỏi bộ học nhưng cùng 'họ' (nạp thẻ, trả tiền...), nên lạc quan hơn. "
             "'Sau luật' là thứ người dùng thấy; 'Rasa thô' chỉ để theo dõi model."),
    "voice_text": ("Speech", "Giọng nói, phần chữ → số tiền/loại/danh mục", voice_text,
                   "Chỉ đo phần SAU khi đã có chữ. Câu đưa vào là chữ viết sẵn, chưa phải kết quả nhận dạng thật của người nói giọng vùng miền."),
    "stt": ("Speech", "Giọng nói → chữ (GIPFormer)", stt,
            "Rất ít file (hiện 13), một giọng/nguồn, tham chiếu suy ra từ tên file (bị cắt) nên chỉ chấm phần đầu câu. "
            "Thêm `Data_Voice/references.tsv` (tên_file<TAB>lời nói đầy đủ) để đo cả câu. Chưa có giọng thật trong môi trường ồn."),
    "ocr": ("OCR", "Hóa đơn: PaddleOCR + VietOCR + bóc tách", ocr,
            "Bộ ảnh này cũng là bộ đã dùng để sửa luật bóc tách, nên số đo lạc quan hơn ảnh lạ. Muốn biết độ chính xác thật cần ảnh chụp mới chưa từng dùng để chỉnh."),
}
GROUPS = ["Chatbot", "Speech", "OCR"]


def measure(key: str, with_ai: bool) -> dict:
    group, title, fn, caveat = BLOCKS[key]
    try:
        rows, details = chat_live(with_ai) if key == "chat_live" else fn()
        error = None
    except Exception as e:  # noqa: BLE001 - một khối hỏng không được làm mất cả bảng điểm
        rows, details, error = [], [], str(e) or type(e).__name__
    judged = [r for r in rows if r["status"] != "—"]
    passed = sum(r["status"] == "ĐẠT" for r in judged)
    if error:
        verdict = "KHÔNG ĐO ĐƯỢC"
    elif not judged:
        verdict = "—"
    else:
        verdict = "ĐẠT" if passed == len(judged) else "CHƯA ĐẠT"
    return {"key": key, "group": group, "title": title, "rows": rows, "details": details, "error": error,
            "caveat": caveat, "passed": passed, "judged": len(judged), "verdict": verdict}


def group_verdict(blocks: list[dict]) -> str:
    if not blocks:
        return "chưa đo"
    if any(b["verdict"] == "KHÔNG ĐO ĐƯỢC" for b in blocks):
        return "KHÔNG ĐO ĐƯỢC"
    judged = sum(b["judged"] for b in blocks)
    passed = sum(b["passed"] for b in blocks)
    return f"{'ĐẠT' if passed == judged else 'CHƯA ĐẠT'} ({passed}/{judged} chỉ tiêu)"


def render(blocks: list[dict]) -> str:
    lines = [f"# Bảng điểm khả thi FinMate", "", f"Đo lúc {datetime.now():%Y-%m-%d %H:%M} bằng `python scripts/scorecard.py`. "
             "Ngưỡng là **đề xuất**, chủ dự án quyết định; \"—\" là chỉ số để tham khảo, chưa có ngưỡng.", "",
             "| Nhóm | Kết quả |", "|---|---|"]
    for g in GROUPS:
        lines.append(f"| {g} | {group_verdict([b for b in blocks if b['group'] == g])} |")
    lines += ["", "ĐẠT trên bộ mẫu nhỏ là điều kiện cần, chưa đủ để kết luận khả thi: xem \"Độ tin cậy\" của từng khối.", ""]
    for b in blocks:
        lines += [f"## {b['group']}: {b['title']} — {b['verdict']}", ""]
        if b["error"]:
            lines += [f"Không đo được: `{b['error']}`", ""]
        else:
            lines += ["| Chỉ tiêu | Đo được | Ngưỡng | |", "|---|---|---|---|"]
            lines += [f"| {r['label']} | {r['value']} | {r['goal']} | {r['status']} |" for r in b["rows"]]
            lines.append("")
        lines += [f"**Độ tin cậy:** {b['caveat']}", ""]
        if b["details"]:
            lines += ["Ca sai tiêu biểu:", ""] + [f"- {d}" for d in b["details"]] + [""]
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--only", default="", help=f"danh sách khối, cách nhau bằng dấu phẩy: {','.join(BLOCKS)}")
    parser.add_argument("--skip", default="", help="khối bỏ qua")
    parser.add_argument("--ai", action="store_true", help="đo thêm chat có Gemini thật")
    parser.add_argument("--no-write", action="store_true", help="chỉ in, không ghi docs/scorecard.md")
    args = parser.parse_args()
    wanted = [k for k in (args.only.split(",") if args.only else BLOCKS) if k]
    unknown = [k for k in wanted + args.skip.split(",") if k and k not in BLOCKS]
    if unknown:
        parser.error(f"khối không có: {unknown}")
    blocks = []
    for key in wanted:
        if key not in args.skip.split(","):
            print(f"đang đo {key} ...", file=sys.stderr, flush=True)
            blocks.append(measure(key, args.ai))
    text = render(blocks)
    if not args.no_write and not args.only and not args.skip:
        OUT.write_text(text + "\n", encoding="utf-8")
        print(f"đã ghi {OUT.relative_to(ROOT)}", file=sys.stderr)
    sys.stdout.reconfigure(encoding="utf-8")
    print(text)
    return 0 if all(b["verdict"] != "KHÔNG ĐO ĐƯỢC" for b in blocks) else 1


if __name__ == "__main__":
    raise SystemExit(main())
