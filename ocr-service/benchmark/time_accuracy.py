"""Đo tốc độ + độ chính xác của pipeline trên data-bill, so sánh chỉ-Paddle với Paddle+VietOCR (đã khởi động nóng).

Chạy:  .\.venv\Scripts\python.exe benchmark\time_accuracy.py
"""
import os
import statistics as st
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
sys.path.insert(0, str(Path(__file__).parent))

import run_databill as rd  # noqa: E402

files = list(rd.EXPECTED)
rd.process(rd.DATA / files[0])  # khởi động nóng: nạp mô hình, không tính vào số đo


def run(viet: bool):
    os.environ["FINMATE_VIETOCR"] = "1" if viet else "0"
    rows = []
    for n in files:
        exp = rd.EXPECTED[n]
        r = rd.process(rd.DATA / n)
        ok_t = r.get("total_amount") == exp["total"]
        ok_m = rd.merchant_ok(exp["merchant"], r.get("display", ""))
        ok_c = r.get("category") == exp["category"]
        rows.append((r["sec"], ok_t, ok_m, ok_c, bool(r.get("needs_review")), ok_t and ok_m and ok_c))
    return rows


for rnd in (1, 2):
    for viet in (False, True):
        rows = run(viet)
        t = sorted(x[0] for x in rows)
        n = len(rows)
        silent = sum((not x[5]) and not x[4] for x in rows)
        print(f"vòng {rnd} {'VietOCR bật ' if viet else 'chỉ Paddle  '}| TB {st.mean(t):.2f}s | trung vị {st.median(t):.2f}s "
              f"| p90 {t[int(n * 0.9) - 1]:.2f}s | max {t[-1]:.2f}s | tổng {sum(x[1] for x in rows)}/{n} "
              f"tên {sum(x[2] for x in rows)}/{n} loại {sum(x[3] for x in rows)}/{n} cả ba {sum(x[5] for x in rows)}/{n} "
              f"âm thầm {silent} nhắc {sum(x[4] for x in rows)}")
print("CPU logic:", os.cpu_count())
