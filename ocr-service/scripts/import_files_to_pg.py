"""Nhập dữ liệu cũ (dataset.jsonl + merchant_memory.json) vào PostgreSQL một lần.

Chạy:  $env:FINMATE_DATABASE_URL = "postgresql://..." ; $env:FINMATE_USER_HASH = "<mã băm user>"
       .\\.venv\\Scripts\\python.exe scripts/import_files_to_pg.py            (xem trước, không ghi gì)
       .\\.venv\\Scripts\\python.exe scripts/import_files_to_pg.py --write    (ghi vào DB)
Chạy lại nhiều lần vẫn an toàn: bill trùng nội dung chỉ giữ một bản, cửa hàng trùng được cập nhật.
Cần đã chạy migrations/0006_*.sql. Không xóa file cũ: giữ làm bản sao lưu.
"""
import os
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent.parent  # thư mục dịch vụ (script nằm trong scripts/): nơi các module và file dữ liệu ở
sys.path.insert(0, str(HERE))

from storage.dataset import Dataset  # noqa: E402
from storage.memory import MerchantMemory, _entry  # noqa: E402
from storage.storage_pg import open_stores  # noqa: E402


def do_import(source_dir: Path, write: bool) -> int:
    """Nhập merchant_memory.json và dataset.jsonl trong source_dir vào DB (source_dir có thể là thư mục được mount vào container)."""
    if not os.environ.get("FINMATE_DATABASE_URL"):
        print("Chưa đặt FINMATE_DATABASE_URL: không biết ghi vào DB nào.")
        return 1
    old_memory, old_dataset = MerchantMemory(source_dir / "merchant_memory.json"), Dataset(source_dir / "dataset.jsonl")
    shops, bills = old_memory._load(), old_dataset.read_all()
    print(f"File cũ trong {source_dir}: {len(shops)} cửa hàng đã nhớ, {len(bills)} bill đã gán nhãn.")
    if not shops and not bills:
        print("Không thấy dataset.jsonl hay merchant_memory.json (hoặc rỗng): kiểm tra thư mục nguồn.")
        return 1
    if not write:
        print("Xem trước thôi. Thêm --write để ghi vào DB.")
        return 0
    memory, dataset = open_stores(source_dir)
    for key, raw in shops.items():
        entry = _entry(raw)  # mục cũ chỉ là chuỗi danh mục; mục mới là {"category", "name"}
        memory.set(key, entry["category"], entry.get("name"))
    for bill in bills:
        dataset.append(bill)
    print(f"Đã ghi. Trong DB hiện có {len(dataset.read_all())} bill quét.")
    return 0


def main(write: bool) -> int:
    return do_import(Path(os.environ.get("FINMATE_IMPORT_DIR", HERE)), write)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main("--write" in sys.argv))
