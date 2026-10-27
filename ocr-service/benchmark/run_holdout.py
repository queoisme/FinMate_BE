"""Cổng đo OCR trên ảnh GIỮ RIÊNG: ảnh chưa từng được dùng để viết hay chỉnh luật bóc tách.

Vì sao có: mọi bộ đáp án hiện có (expected*.json, 12 bill ngày 2026-10-14...) đã được dùng để chỉnh luật và ngưỡng (xem data/thresholds.json), nên số đo
trên đó là lạc quan. Chỉ có ảnh mới, chưa ai nhìn kết quả của nó, mới cho biết độ chính xác với ảnh lạ.

Quy tắc, không có ngoại lệ:
  1. Ảnh trong bộ này CHỈ để đo. Thấy sai thì ghi lại nguyên tắc còn thiếu, KHÔNG sửa luật cho riêng ảnh đó.
  2. Nếu một ảnh bị dùng để sửa luật thì chuyển nó sang bộ chỉnh (expected_databill.json), và bộ giữ riêng phải được bổ sung ảnh mới.
  3. Dưới MIN_IMAGES ảnh thì không kết luận gì (mã thoát 2): vài ảnh không cho biết gì về ảnh lạ.

Thêm ảnh: bỏ vào benchmark/holdout/ (hoặc đặt HOLDOUT_DIR) và khai báo đáp án trong benchmark/expected_holdout.json, cùng lược đồ expected_databill.json:
  {"tên-ảnh.jpg": {"kind": "mô tả", "merchant": "chuỗi viết liền bỏ dấu | [mảng] | null", "total": 123000, "category": "dining"}}
Ảnh chứa thông tin cá nhân: giữ cục bộ, không đưa lên nơi công khai.

Chạy (như run_databill.py):  python benchmark/run_holdout.py [--verbose]     Mã thoát: 0 đạt, 1 không đạt, 2 chưa đủ ảnh để kết luận.
"""
import json
import os
import sys
from pathlib import Path

HERE = Path(__file__).parent
sys.path.insert(0, str(HERE.parent))
sys.path.insert(0, str(HERE))
sys.stdout.reconfigure(encoding="utf-8")

import run_databill  # noqa: E402  (process() và merchant_ok() dùng chung với bộ chỉnh)

HOLDOUT = Path(os.environ.get("HOLDOUT_DIR") or HERE / "holdout")
EXPECTED_FILE = HERE / "expected_holdout.json"
MIN_IMAGES = 30  # dưới mức này không kết luận
MAX_SILENT_RATE = 0.02  # lỗi âm thầm (sai mà app không nhắc kiểm tra) tối đa 2%, cùng ngưỡng ở docs/scorecard.md


def main() -> int:
    if not EXPECTED_FILE.is_file():
        print(f"Chưa có {EXPECTED_FILE.name}: CHƯA CÓ bộ ảnh giữ riêng nên chưa thể nói độ chính xác với ảnh lạ. Xem hướng dẫn đầu tệp này.")
        return 2
    expected = {k: v for k, v in json.loads(EXPECTED_FILE.read_text(encoding="utf-8")).items() if not k.startswith("_")}
    rows = []
    for name, exp in expected.items():
        r = run_databill.process(HOLDOUT / name)
        if "rejected" in r:
            rows.append(dict(name=name, total_ok=False, merchant_ok=False, cat_ok=False, review=True, rejected=r["rejected"]))
        else:
            rows.append(dict(name=name, total_ok=r["total_amount"] == exp["total"], merchant_ok=run_databill.merchant_ok(exp["merchant"], r["display"]),
                             cat_ok=r["category"] == exp["category"], review=bool(r["needs_review"])))
        row = rows[-1]
        row["all_ok"] = row["total_ok"] and row["merchant_ok"] and row["cat_ok"]
        row["silent"] = (not row["all_ok"]) and not row["review"]
        if "--verbose" in sys.argv or not row["all_ok"]:
            print(f"[{'OK ' if row['all_ok'] else ('ÂM THẦM' if row['silent'] else 'nhắc  ')}] {name[:40]}")
    n = len(rows)
    if n == 0:
        print("expected_holdout.json không có ảnh nào.")
        return 2
    rate = lambda key: f"{sum(r[key] for r in rows)}/{n} ({sum(r[key] for r in rows) / n:.0%})"
    print(f"\n=== {n} ảnh GIỮ RIÊNG ===\ntổng tiền đúng: {rate('total_ok')}\ntên cửa hàng đúng: {rate('merchant_ok')}\ndanh mục đúng: {rate('cat_ok')}\n"
          f"đúng cả ba: {rate('all_ok')}\nLỖI ÂM THẦM: {rate('silent')}")
    if n < MIN_IMAGES:
        print(f"\nCHỈ {n} ảnh (< {MIN_IMAGES}): chưa đủ để kết luận về ảnh lạ.")
        return 2
    silent_rate = sum(r["silent"] for r in rows) / n
    if silent_rate > MAX_SILENT_RATE:
        print(f"\nKHÔNG ĐẠT: lỗi âm thầm {silent_rate:.1%} > {MAX_SILENT_RATE:.0%}")
        return 1
    print("\nĐẠT")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
