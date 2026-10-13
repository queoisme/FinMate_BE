#!/usr/bin/env python3
"""Dạy bảng nhớ tên món (``bill_item_cache``) từ file nhãn đã được người kiểm: lần sau tên đó (kể cả dạng OCR đọc sai chữ) ra đúng nhóm ngay,
không cần hỏi AI và không phụ thuộc luật từ khóa.

Chạy trong container ai-service (có kết nối DB):
    python scripts/learn_items.py tests/fixtures/eval/item_cases.jsonl --from-line 73            (xem trước, không ghi gì)
    python scripts/learn_items.py tests/fixtures/eval/item_cases.jsonl --from-line 73 --write    (ghi vào DB)

Mỗi dòng JSON: {"name": "...", "expect": "<slug>"} (các khóa khác, như "merchant", được bỏ qua). Chạy lại nhiều lần vẫn an toàn: cùng một khóa thì nhãn mới thay nhãn cũ.
Chỉ nên đưa vào nhãn do người đọc từ ảnh thật; nhãn sai ở đây sai mãi cho đến khi sửa trong DB.
"""

from __future__ import annotations

import argparse
import asyncio
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from app.bill.item_classifier import learnable_items  # noqa: E402
from app.bill.knowledge import DbBillKnowledge  # noqa: E402


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("file")
    parser.add_argument("--from-line", type=int, default=1, help="bắt đầu từ dòng này (đếm từ 1)")
    parser.add_argument("--write", action="store_true")
    args = parser.parse_args()
    lines = pathlib.Path(args.file).read_text(encoding="utf-8").splitlines()[args.from_line - 1:]
    cases = [json.loads(line) for line in lines if line.strip()]
    learned = learnable_items(cases)
    print(f"{len(cases)} nhãn đọc được, {len(learned)} tên đủ điều kiện để nhớ.")
    for key, slug in learned.items():
        print(f"  {key!r} -> {slug}")
    if not args.write:
        print("Xem trước thôi. Thêm --write để ghi vào DB.")
        return 0
    asyncio.run(DbBillKnowledge().remember_items(learned))
    print("Đã ghi.")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
