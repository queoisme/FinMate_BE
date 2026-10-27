"""Nạp dữ liệu cho AI Service một lần (qua API của nó, không kết nối DB): thương hiệu/từ khóa, dữ liệu đã huấn luyện.

Schema do Alembic của ai-service quản lý: AI Service phải đang chạy (nó tự `alembic upgrade head` lúc khởi động).
Chạy:  python scripts/db_setup.py [--seed] [--import] [--write]
  --seed      nạp thương hiệu và từ khóa đang viết trong code vào DB (seed_knowledge.py)
  --import    nhập dataset.jsonl và merchant_memory.json vào DB; thư mục nguồn lấy từ FINMATE_IMPORT_DIR (mặc định: thư mục này)
  --gold      đánh dấu bộ chuẩn (gold) từ benchmark/expected_databill.json; ảnh lấy từ FINMATE_GOLD_DIR/data-bill (mặc định: thư mục ai-service)
  --normalize-gold  đổi tên bill gold từ mẫu viết liền ("longchau") sang tên hiển thị của cùng cửa hàng ("Nhà thuốc Long Châu") để phép đo không bị rò rỉ
  --synthetic nhập bill giả qua OCR (dataset_synthetic_ocr.jsonl) với nhãn synthetic_ocr: chỉ để học, không tính vào số đo
  --write     ghi thật; không có cờ này chỉ in ra việc sẽ làm
Cần AI_SERVICE_URL và AI_SERVICE_API_KEY. Chạy lại an toàn: seed/import không tạo bản trùng.
"""
import json
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent  # thư mục dịch vụ (script nằm trong scripts/): nơi các module và file dữ liệu ở
sys.path.insert(0, str(ROOT))

from storage import datasets_api  # noqa: E402
import import_files  # noqa: E402
from storage import knowledge_api  # noqa: E402
from storage.ai_api import api_from_env  # noqa: E402

def main(argv: list[str]) -> int:
    api = api_from_env()
    if not api:
        print("Chưa đặt AI_SERVICE_URL và AI_SERVICE_API_KEY: không biết nạp vào AI Service nào.")
        return 1
    write = "--write" in argv
    if not write:
        print("Xem trước thôi: thêm --write để ghi thật.")
    if "--seed" in argv:
        if write:
            added = knowledge_api.seed(api)
            print(f"Nạp kiến thức: thêm {added['brands']} thương hiệu, {added['keywords']} từ khóa.")
        else:
            print("Sẽ thêm các thương hiệu và từ khóa còn thiếu từ data/*.json vào DB (xem chi tiết: scripts/seed_knowledge.py).")
    if "--import" in argv:
        code = import_files.do_import(Path(os.environ.get("FINMATE_IMPORT_DIR", ROOT)), write)
        if code:
            return code
    base = Path(os.environ.get("FINMATE_GOLD_DIR", ROOT.parent))
    if "--gold" in argv:
        expected = {k: v for k, v in json.loads((base / "bill-api" / "benchmark" / "expected_databill.json").read_text(encoding="utf-8")).items()
                    if not k.startswith("_")}

        def lines_of(name: str) -> list[str]:  # OCR lười: chỉ chạy cho ảnh chưa có trong DB
            from reading import ocr
            from parsing.privacy import sanitize_lines
            return sanitize_lines([t for t, _ in ocr.read_lines((base / "data-bill" / name).read_bytes())])

        r = datasets_api.import_gold(api, expected, lines_of, write=write)
        verb = "đã" if write else "sẽ"
        print(f"Bộ chuẩn (gold): {verb} đánh dấu {r['marked']} bill có sẵn, {verb} thêm {r['added']} bill mới, {r['already']} bill đã là gold từ trước.")
    if "--normalize-gold" in argv:
        from classification.brands import BRAND_DISPLAY
        for old, new in datasets_api.normalize_gold_names(api, BRAND_DISPLAY, write=write):
            print(f"  gold: {old!r} -> {new!r}")
        print("Đã đổi tên gold." if write else "Xem trước tên gold sẽ đổi (chưa ghi).")
    if "--synthetic" in argv:
        r = datasets_api.import_synthetic(api, Path(os.environ.get("FINMATE_IMPORT_DIR", ROOT)) / "dataset_synthetic_ocr.jsonl", write=write)
        print(f"Bill giả qua OCR: {r['in_file']} trong file, {r['in_db']} trong DB{'' if write else ' (xem trước, chưa ghi)'}.")
    print("Xong." if write else "Chưa ghi gì (không có --write).")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main(sys.argv[1:]))
