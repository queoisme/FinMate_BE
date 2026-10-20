r"""Đồng bộ thương hiệu và từ khóa mặc định (data/*.json) vào PostgreSQL: chỉ THÊM mục DB còn thiếu, DB luôn thắng.

Chạy:  $env:FINMATE_DATABASE_URL = "postgresql://..."
       .\.venv\Scripts\python.exe scripts/seed_knowledge_to_pg.py            (xem trước: liệt kê mục còn thiếu, không ghi gì)
       .\.venv\Scripts\python.exe scripts/seed_knowledge_to_pg.py --write    (thêm các mục còn thiếu)
Chạy lại bao nhiêu lần cũng an toàn và chạy ở môi trường nào cũng ra cùng kết quả: dòng đã có (đã sửa, đã tắt is_active = false, hoặc từ khóa đã chuyển sang nhóm khác) không bị ghi đè, bật lại hay thêm lại.
Chạy sau mỗi lần deploy có sửa data/*.json (thêm chuỗi mới vào file thì DB cũ chỉ nhận được qua bước này). Cần đã chạy `alembic upgrade head`.
Muốn bỏ một mục thì TẮT (is_active = false) trong DB, đừng xóa dòng: xóa rồi seed sẽ thêm lại.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))  # thư mục dịch vụ (script nằm trong scripts/): nơi các module và file dữ liệu ở

from storage import knowledge_pg  # noqa: E402
from storage.storage_pg import connect_from_env  # noqa: E402


def _show(plan: dict, limit: int = 15) -> None:
    for label, items in (("thương hiệu", plan["brands"]), ("từ khóa", [" / ".join(k) for k in plan["keywords"]])):
        print(f"  {len(items)} {label} còn thiếu trong DB" + (":" if items else "."))
        for item in items[:limit]:
            print(f"    - {item}")
        if len(items) > limit:
            print(f"    ... và {len(items) - limit} mục nữa")


def main(write: bool) -> int:
    connect = connect_from_env()
    if not connect:
        print("Chưa đặt FINMATE_DATABASE_URL: không biết ghi vào DB nào.")
        return 1
    try:
        plan = knowledge_pg.missing(connect)
    except Exception as e:  # noqa: BLE001 - thường là chưa có bảng: nói rõ cách xử lý thay vì in traceback
        print(f"Không đọc được bảng bill_brands/bill_keywords ({type(e).__name__}): đã chạy `alembic upgrade head` chưa?")
        return 1
    print("So data/*.json với DB:")
    _show(plan)
    if not write:
        print("Xem trước thôi. Thêm --write để thêm các mục còn thiếu.")
        return 0
    added = knowledge_pg.seed(connect)
    print(f"Đã thêm mới vào DB: {added['brands']} thương hiệu, {added['keywords']} từ khóa (dòng đã có thì giữ nguyên).")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main("--write" in sys.argv))
