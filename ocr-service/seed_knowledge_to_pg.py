"""Nạp thương hiệu và từ khóa đang viết trong code vào PostgreSQL một lần (sau đó DB là nơi sửa).

Chạy:  $env:FINMATE_DATABASE_URL = "postgresql://..."
       .\\.venv\\Scripts\\python.exe seed_knowledge_to_pg.py            (xem trước, không ghi gì)
       .\\.venv\\Scripts\\python.exe seed_knowledge_to_pg.py --write    (ghi vào DB)
Chạy lại nhiều lần vẫn an toàn: dòng đã có (kể cả đã sửa trong DB) không bị ghi đè. Cần đã chạy migrations/0007_*.sql.
"""
import sys

import brands
import extract
import knowledge_pg
from storage_pg import connect_from_env


def main(write: bool) -> int:
    connect = connect_from_env()
    if not connect:
        print("Chưa đặt FINMATE_DATABASE_URL: không biết ghi vào DB nào.")
        return 1
    keywords = sum(len(w) for t in (extract._CATEGORY_KEYWORDS, extract._PRODUCT_KEYWORDS) for w in t.values())
    print(f"Trong code: {len(brands.BRAND_CATEGORIES)} thương hiệu, {keywords} từ khóa.")
    if not write:
        print("Xem trước thôi. Thêm --write để ghi vào DB.")
        return 0
    added = knowledge_pg.seed(connect)
    print(f"Đã thêm mới vào DB: {added['brands']} thương hiệu, {added['keywords']} từ khóa (dòng đã có thì giữ nguyên).")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main("--write" in sys.argv))
