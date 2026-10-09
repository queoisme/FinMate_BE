"""Gom phiếu bầu của người dùng thành thương hiệu mới trong DB (xem promotion_pg.py cho quy tắc ngưỡng).

Chạy:  $env:FINMATE_DATABASE_URL = "postgresql://..."
       .\\.venv\\Scripts\\python.exe promote_knowledge.py            (xem trước, không ghi gì)
       .\\.venv\\Scripts\\python.exe promote_knowledge.py --write    (ghi vào DB)
Sau khi ghi: POST /admin/reload-knowledge (hoặc khởi động lại API) để API dùng thương hiệu mới. Chạy theo lịch (cron) cũng được.
Cần đã chạy migrations 0006-0008.
"""
import sys

from promotion_pg import MIN_AGREEMENT, MIN_CANDIDATE_USERS, MIN_USERS, promote
from storage_pg import connect_from_env

LABELS = {"promoted": "Bật ngay (đủ phiếu bầu, đồng thuận cao)", "upgraded": "Từ chờ lên bật (đủ phiếu bầu)",
          "pending": "Ghi ở trạng thái chờ (chưa đủ ngưỡng, cần duyệt)", "disputes": "Tranh chấp với dòng đã có (không ghi đè, cần duyệt)",
          "skipped": "Bỏ qua (khóa quá ngắn/là mã, hoặc đã có thương hiệu bao phủ)"}


def main(write: bool) -> int:
    connect = connect_from_env()
    if not connect:
        print("Chưa đặt FINMATE_DATABASE_URL: không biết đọc DB nào.")
        return 1
    print(f"Ngưỡng: bật ngay khi >= {MIN_USERS} người cùng chọn và >= {MIN_AGREEMENT:.0%} đồng thuận; chờ duyệt từ {MIN_CANDIDATE_USERS} người.")
    result = promote(connect, write)
    for key, label in LABELS.items():
        print(f"{label}: {len(result[key])}" + (f"  {result[key][:15]}" if result[key] else ""))
    print("Đã ghi vào DB." if write else "Xem trước thôi. Thêm --write để ghi vào DB.")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main("--write" in sys.argv))
