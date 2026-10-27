"""Gom phiếu bầu của người dùng thành thương hiệu mới ở AI Service (xem storage/promotion_api.py cho quy tắc ngưỡng).

Chạy:  $env:AI_SERVICE_URL = "http://localhost:8000" ; $env:AI_SERVICE_API_KEY = "<INTERNAL_API_KEY của ai-service>"
       .\\.venv\\Scripts\\python.exe scripts/promote_knowledge.py            (xem trước, không ghi gì)
       .\\.venv\\Scripts\\python.exe scripts/promote_knowledge.py --write    (ghi vào DB)
Sau khi ghi: POST /admin/reload-knowledge (hoặc khởi động lại API) để API dùng thương hiệu mới. Chạy theo lịch (cron) cũng được.
Phiếu lấy từ các bill người dùng ĐÃ đồng ý đóng góp (view v_merchant_consensus của AI Service).
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))  # thư mục dịch vụ (script nằm trong scripts/): nơi các module và file dữ liệu ở

from storage.ai_api import api_from_env  # noqa: E402
from storage.promotion_api import MIN_AGREEMENT, MIN_CANDIDATE_USERS, MIN_USERS, promote  # noqa: E402

LABELS = {"promoted": "Bật ngay (đủ phiếu bầu, đồng thuận cao)", "upgraded": "Từ chờ lên bật (đủ phiếu bầu)",
          "pending": "Ghi ở trạng thái chờ (chưa đủ ngưỡng, cần duyệt)", "disputes": "Tranh chấp với dòng đã có (không ghi đè, cần duyệt)",
          "skipped": "Bỏ qua (khóa quá ngắn/là mã, hoặc đã có thương hiệu bao phủ)"}


def main(write: bool) -> int:
    api = api_from_env()
    if not api:
        print("Chưa đặt AI_SERVICE_URL và AI_SERVICE_API_KEY: không biết gọi AI Service nào.")
        return 1
    print(f"Ngưỡng: bật ngay khi >= {MIN_USERS} người cùng chọn và >= {MIN_AGREEMENT:.0%} đồng thuận; chờ duyệt từ {MIN_CANDIDATE_USERS} người.")
    result = promote(api, write)
    for key, label in LABELS.items():
        print(f"{label}: {len(result[key])}" + (f"  {result[key][:15]}" if result[key] else ""))
    print("Đã ghi vào AI Service." if write else "Xem trước thôi. Thêm --write để ghi.")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main("--write" in sys.argv))
