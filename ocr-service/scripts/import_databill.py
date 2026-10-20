"""Nhập các ảnh trong ai-service/data-bill vào dataset.jsonl làm dữ liệu huấn luyện (nhãn lấy từ benchmark/expected_databill.json).

Chạy:  .\.venv\Scripts\python.exe scripts/import_databill.py            (xem trước, không ghi gì)
       .\.venv\Scripts\python.exe scripts/import_databill.py --write    (sao lưu dataset rồi ghi thêm)

- Nhãn là phán đoán của người soạn expected_databill.json, đánh dấu label_source = "imported" để phân biệt với nhãn người dùng.
- Bỏ ảnh không có tên cửa hàng đọc được, và ảnh trùng cửa hàng đã có trong dataset. Không đụng tới merchant_memory.json.
"""
import json
import shutil
import sys
import time
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
HERE = Path(__file__).resolve().parent.parent  # thư mục dịch vụ (script nằm trong scripts/): nơi các module và file dữ liệu ở
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE / "benchmark"))

import run_databill as rd  # noqa: E402
from classification.brands import BRAND_DISPLAY  # noqa: E402
from storage.dataset import Dataset, redact_line  # noqa: E402
from extract import _alnum, _norm  # noqa: E402

DATASET = HERE / "dataset.jsonl"


def key(text: str) -> str:
    return _alnum(_norm(text or ""))


def main(write: bool) -> None:
    ds = Dataset(DATASET)
    existing = [key(r.get("merchant", "")) for r in ds.read_all()]
    plan = []
    for name, exp in rd.EXPECTED.items():
        wanted = exp["merchant"]
        if wanted is None:
            plan.append((name, None, "bỏ: không có tên cửa hàng đọc được"))
            continue
        subs = wanted if isinstance(wanted, list) else [wanted]
        if any(s in e for s in subs for e in existing):
            plan.append((name, None, "bỏ: cửa hàng đã có trong dataset"))
            continue
        if any(p[1] and p[1]["total_amount"] == exp["total"] and subs[0] in key(p[1]["merchant"]) for p in plan):
            plan.append((name, None, "bỏ: trùng hóa đơn đã nhập (cùng cửa hàng, cùng tổng)"))
            continue
        r = rd.process(rd.DATA / name)
        if "rejected" in r:
            plan.append((name, None, f"bỏ: OCR từ chối ({r['rejected']})"))
            continue
        label = r["display"] if rd.merchant_ok(wanted, r["display"]) else (BRAND_DISPLAY.get(subs[0]) or subs[0].title())
        row = {
            "merchant": label, "ocr_merchant": r["merchant"], "category": exp["category"], "label_source": "imported",
            "predicted_category": r["category"], "predicted_source": r["category_source"], "total_amount": exp["total"],
            "lines": [redact_line(l[:200]) for l in r["lines"][:200]],
        }
        plan.append((name, row, "nhập"))
    todo = [p for p in plan if p[1]]
    for name, row, note in plan:
        print(f"{name[:8]}  {note:40s}", f"{row['merchant']!r} -> {row['category']} ({row['total_amount']})" if row else "")
    cats = {}
    for _, row, _ in todo:
        cats[row["category"]] = cats.get(row["category"], 0) + 1
    print(f"\nSẽ nhập {len(todo)} bản ghi, theo danh mục: {cats}")
    if not write:
        print("(xem trước — thêm --write để ghi)")
        return
    shutil.copy2(DATASET, f"{DATASET}.bak-{time.strftime('%Y%m%d-%H%M%S')}")
    for _, row, _ in todo:
        ds.append(row)
    print("Đã ghi. Dataset hiện có", len(ds.read_all()), "bản ghi.")


if __name__ == "__main__":
    main("--write" in sys.argv)
