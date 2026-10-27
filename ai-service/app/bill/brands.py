"""Cửa hàng/chuỗi hay gặp -> nhóm mặc định của bộ luật bill (9 nhóm, khóa đã bỏ dấu, chữ thường).

Dữ liệu KHÔNG viết ở đây: bảng thương hiệu dùng chung với ocr-service (``ocr-service/data/brands.json``, lý do từng mục ở ``data/NOTES.md``) và được nạp
qua ``lexicon.py``. Thương hiệu có nhóm ngoài 9 nhóm của bộ luật bill (Gia đình, Giáo dục) quy thẳng ra slug backend ở ``app/data/brand_backend_slug.json``.
Khi có DB, các bảng được nạp lại từ ``kb_brands`` và sửa TẠI CHỖ (``knowledge.py``) nên phải giữ nguyên dict/set.
"""
import json
from pathlib import Path

from app.bill import lexicon

_DATA = lexicon.load("brands.json")
BRAND_CATEGORIES = {b["key"]: b["category"] for b in _DATA["brands"]}
BRAND_DISPLAY = {b["key"]: b["display"] for b in _DATA["brands"]}  # tên chuẩn (có dấu) để hiển thị và lưu
WHOLE_WORD_BRANDS = set(_DATA["whole_word"])  # tên ngắn / từ thông dụng chỉ khớp khi đứng thành cả từ; set (không phải frozenset) vì knowledge_pg cập nhật tại chỗ
BRAND_BACKEND_SLUG: dict[str, str] = json.loads((Path(__file__).resolve().parents[1] / "data" / "brand_backend_slug.json").read_text(encoding="utf-8"))
