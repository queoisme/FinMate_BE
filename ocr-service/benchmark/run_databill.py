"""Đo toàn bộ pipeline trên ảnh thật trong ai-service/data-bill (khởi đầu nguội: KHÔNG dùng bộ nhớ cửa hàng đã lưu).

Chạy:  .\.venv\Scripts\python.exe benchmark\run_databill.py [--verbose] [--fail-only] [--ai]
  --ai: thêm bước nhờ AI (cần GEMINI_API_KEY; chỉ gửi chữ đã lọc) để so sánh trước/sau khi bật

Các chỉ số:
  - tổng đúng, tên đúng, danh mục đúng
  - đúng cả ba
  - LỖI ÂM THẦM: sai ít nhất một trong ba mà app KHÔNG nhắc kiểm tra (needs_review = False)  <- chỉ số quan trọng nhất
  - nhắc kiểm tra nhầm: đúng cả ba mà vẫn nhắc (phiền người dùng nhưng không hại)
"""
import json
import os
import sys
import time
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
sys.stdout.reconfigure(encoding="utf-8")

import ocr                                              # noqa: E402
from totals import choose_total                          # noqa: E402
from items import payable_total                          # noqa: E402
from extract import CONFIDENT_RECEIPT_SCORE, MIN_RECEIPT_SCORE, _alnum, _norm, analyze, receipt_score  # noqa: E402
from merchant_name import format_merchant_name          # noqa: E402
from privacy import sanitize_lines                      # noqa: E402
from quality import assess_quality, image_quality       # noqa: E402

HERE = Path(__file__).parent
DATA = Path(os.environ.get("DATA_BILL") or HERE.parents[1] / "data-bill")
EXPECTED = {k: v for k, v in json.loads((HERE / "expected_databill.json").read_text(encoding="utf-8")).items() if not k.startswith("_")}


def merchant_ok(expected, display: str) -> bool:
    got = _alnum(_norm(display or ""))
    if expected is None:
        return not got
    wanted = expected if isinstance(expected, list) else [expected]
    return any(w in got for w in wanted)


def process(path: Path) -> dict:
    """Giống luồng trong main.py nhưng không dùng bộ nhớ cửa hàng."""
    data = path.read_bytes()
    t0 = time.time()
    quality = image_quality(data)
    try:
        boxes = ocr.read_boxes(data)
        lines = [(b.text, b.conf) for b in boxes]
    except ocr.NoTextError:
        return {"rejected": "no_text", "sec": time.time() - t0}
    score = receipt_score([t for t, _ in lines])
    if score < MIN_RECEIPT_SCORE:
        return {"rejected": "not_a_receipt", "score": score, "sec": time.time() - t0}
    total, verified = choose_total(boxes, fallback=payable_total(boxes))  # như main.analyze_bill
    r = analyze(lines, forced_total=total)
    r["total_verified"] = verified
    r["needs_review"] = r["needs_review"] or quality != "ok" or score < CONFIDENT_RECEIPT_SCORE
    r["display"] = format_merchant_name(r["merchant"]) or (r["merchant"] or "")
    r.update({"quality": quality, "score": score, "sec": time.time() - t0, "lines": [t for t, _ in lines]})
    return r


def main() -> None:
    verbose, fail_only = "--verbose" in sys.argv, "--fail-only" in sys.argv
    rows = []
    for name, exp in EXPECTED.items():
        r = process(DATA / name)
        if "rejected" in r:
            row = dict(name=name, kind=exp["kind"], total_ok=False, merchant_ok=False, cat_ok=False, review=True,
                       got_total=None, got_merchant=None, got_cat=None, sec=r["sec"], rejected=r["rejected"], verified=False)
        else:
            row = dict(name=name, kind=exp["kind"], total_ok=r["total_amount"] == exp["total"],
                       merchant_ok=merchant_ok(exp["merchant"], r["display"]),
                       cat_ok=r["category"] == exp["category"], review=bool(r["needs_review"]),
                       got_total=r["total_amount"], got_merchant=r["display"] or None, got_cat=r["category"],
                       sec=r["sec"], src=r["category_source"], verified=bool(r.get("total_verified")))
        row["all_ok"] = row["total_ok"] and row["merchant_ok"] and row["cat_ok"]
        row["silent"] = (not row["all_ok"]) and not row["review"]
        rows.append(row)
        if verbose or (fail_only and not row["all_ok"]):
            flag = "OK " if row["all_ok"] else ("ÂM THẦM" if row["silent"] else "nhắc  ")
            print(f"[{flag}] {name[:8]} {exp['kind'][:46]:46s} | tổng {'ĐÚNG' if row['total_ok'] else 'sai '} {row['got_total']} (đúng {exp['total']}) "
                  f"| tên {'ĐÚNG' if row['merchant_ok'] else 'sai '} {row['got_merchant']!r} | loại {'ĐÚNG' if row['cat_ok'] else 'sai '} {row['got_cat']}(đúng {exp['category']})"
                  + (f" | TỪ CHỐI: {row['rejected']}" if row.get("rejected") else ""))
    n = len(rows)
    pct = lambda k: f"{sum(r[k] for r in rows)}/{n} ({sum(r[k] for r in rows)/n:.0%})"
    print(f"\n=== {n} ảnh thật (khởi đầu nguội, không dùng bộ nhớ) ===")
    print(f"tổng tiền đúng    : {pct('total_ok')}")
    print(f"tên cửa hàng đúng : {pct('merchant_ok')}")
    print(f"danh mục đúng     : {pct('cat_ok')}")
    print(f"đúng cả ba        : {pct('all_ok')}")
    print(f"LỖI ÂM THẦM       : {pct('silent')}   <- sai mà app không nhắc kiểm tra")
    print(f"nhắc kiểm tra     : {pct('review')}  (trong đó nhắc dù đúng cả ba: {sum(r['all_ok'] and r['review'] for r in rows)})")
    ver = [r for r in rows if r["verified"]]
    unver = [r for r in rows if not r["verified"]]
    print(f"tổng ĐÃ kiểm chứng bằng số học: {len(ver)}/{n}, trong đó đúng {sum(r['total_ok'] for r in ver)}; CHƯA kiểm chứng: {len(unver)}, trong đó đúng {sum(r['total_ok'] for r in unver)}")
    print(f"thời gian TB      : {sum(r['sec'] for r in rows)/n:.1f} giây/ảnh")


if __name__ == "__main__":
    main()
