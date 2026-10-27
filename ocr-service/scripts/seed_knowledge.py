r"""Đồng bộ thương hiệu và từ khóa mặc định (data/*.json) vào AI Service (qua API, không kết nối DB): chỉ THÊM mục còn thiếu, AI Service luôn thắng.

Chạy:  $env:AI_SERVICE_URL = "http://localhost:8000" ; $env:AI_SERVICE_API_KEY = "<INTERNAL_API_KEY của ai-service>"
       .\.venv\Scripts\python.exe scripts/seed_knowledge.py            (xem trước: liệt kê mục còn thiếu, không ghi gì)
       .\.venv\Scripts\python.exe scripts/seed_knowledge.py --write    (thêm các mục còn thiếu)
Chạy lại bao nhiêu lần cũng an toàn và chạy ở môi trường nào cũng ra cùng kết quả: dòng đã có (đã sửa, đã tắt is_active = false, hoặc từ khóa đã chuyển sang nhóm khác) giữ nguyên.
Chạy sau mỗi lần deploy có sửa data/*.json (thêm chuỗi mới vào file thì AI Service cũ chỉ nhận được qua bước này). AI Service phải đang chạy (nó tự migrate lúc khởi động).
Muốn bỏ một mục thì TẮT (is_active = false) ở AI Service, đừng xóa dòng: xóa rồi seed sẽ thêm lại.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))  # thư mục dịch vụ (script nằm trong scripts/): nơi các module và file dữ liệu ở

from storage import knowledge_api  # noqa: E402
from storage.ai_api import AiApiError, api_from_env  # noqa: E402


def _show(plan: dict, limit: int = 15) -> None:
    for label, items in (("thương hiệu", plan["brands"]), ("từ khóa", [" / ".join(k) for k in plan["keywords"]])):
        print(f"  {len(items)} {label} còn thiếu ở AI Service" + (":" if items else "."))
        for item in items[:limit]:
            print(f"    - {item}")
        if len(items) > limit:
            print(f"    ... và {len(items) - limit} mục nữa")


def main(write: bool) -> int:
    api = api_from_env()
    if not api:
        print("Chưa đặt AI_SERVICE_URL và AI_SERVICE_API_KEY: không biết ghi vào AI Service nào.")
        return 1
    try:
        plan = knowledge_api.missing(api)
    except AiApiError as e:  # AI Service chưa lên hay sai khóa: nói rõ thay vì in traceback
        print(f"Không đọc được kiến thức từ AI Service ({e}): AI Service đã chạy và AI_SERVICE_API_KEY đúng chưa?")
        return 1
    print("So data/*.json với AI Service:")
    _show(plan)
    if not write:
        print("Xem trước thôi. Thêm --write để thêm các mục còn thiếu.")
        return 0
    added = knowledge_api.seed(api)
    print(f"Đã thêm mới: {added['brands']} thương hiệu, {added['keywords']} từ khóa (dòng đã có thì giữ nguyên).")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main("--write" in sys.argv))
