"""Đọc bảng dữ liệu mặc định trong thư mục data/ ở gốc dịch vụ (đường dẫn tính theo vị trí file, không theo thư mục chạy)."""
import json
from pathlib import Path

DATA_DIR = Path(__file__).resolve().parent.parent / "data"


def load(name: str):
    """Đọc data/<name>. Thiếu file thì báo lỗi ngay: chạy với bảng rỗng sẽ phân loại sai mà không ai biết."""
    with open(DATA_DIR / name, encoding="utf-8") as f:
        return json.load(f)


def keyword_table(name: str) -> dict[str, tuple[str, ...]]:
    """Bảng nhóm -> các từ khóa. Thứ tự nhóm trong file là thứ tự ưu tiên; giá trị là tuple như bản viết cứng trước đây."""
    return {category: tuple(words) for category, words in load(name).items()}
