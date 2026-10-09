"""Làm sạch dữ liệu cá nhân trong dataset.jsonl đã lưu từ trước (dùng cùng bộ che với lúc lưu mới).

Chạy:  .\.venv\Scripts\python.exe clean_dataset.py            (xem trước, không ghi)
       .\.venv\Scripts\python.exe clean_dataset.py --write    (sao lưu dataset rồi ghi đè bản đã làm sạch)
Chạy nhiều lần cho kết quả như nhau.
"""
import json
import shutil
import sys
import time
from pathlib import Path

from privacy import sanitize_lines

PATH = Path(__file__).parent / "dataset.jsonl"


def main(write: bool) -> None:
    rows, before, after = [], 0, 0
    for line in PATH.read_text(encoding="utf-8-sig").splitlines():
        if not line.strip():
            continue
        r = json.loads(line)
        old = r.get("lines", [])
        r["lines"] = sanitize_lines(old)
        before, after = before + len(old), after + len(r["lines"])
        rows.append(r)
    print(f"{len(rows)} hóa đơn: {before} dòng chữ -> {after} dòng (bỏ/che {before - after} dòng, che thêm trong các dòng còn lại)")
    if not write:
        print("(xem trước — thêm --write để ghi)")
        return
    shutil.copy2(PATH, f"{PATH}.bak-{time.strftime('%Y%m%d-%H%M%S')}")
    PATH.write_text("\n".join(json.dumps(r, ensure_ascii=False) for r in rows) + "\n", encoding="utf-8")
    print("Đã ghi (bản gốc đã sao lưu).")


if __name__ == "__main__":
    main("--write" in sys.argv)
