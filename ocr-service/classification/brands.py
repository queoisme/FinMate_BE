"""Cửa hàng/chuỗi hay gặp -> danh mục mặc định (khóa đã bỏ dấu, chữ thường).

Dữ liệu nằm ở data/brands.json (kèm lý do từng mục trong data/NOTES.md), không viết cứng ở đây.
Khóa phải đặc trưng (>= 5 chữ, đã bỏ dấu) vì khớp theo chuỗi con; tên ngắn hoặc là từ thông dụng thì đưa vào "whole_word" (chỉ khớp khi đứng thành cả từ).
Khi có DB, các bảng này được nạp từ kb_brands thay cho bản mặc định (xem storage/knowledge_api.py), và được cập nhật TẠI CHỖ nên phải giữ nguyên dict/set.
"""
from classification import data_files

_DATA = data_files.load("brands.json")
BRAND_CATEGORIES = {b["key"]: b["category"] for b in _DATA["brands"]}
BRAND_DISPLAY = {b["key"]: b["display"] for b in _DATA["brands"]}  # tên chuẩn (có dấu) để hiển thị và lưu
WHOLE_WORD_BRANDS = set(_DATA["whole_word"])  # set (không phải frozenset): knowledge_api cập nhật tại chỗ từ DB
