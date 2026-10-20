"""Từ vựng cho câu chat/giọng nói ngắn ("đi ăn 200k", "mua sắm 1 triệu"): bổ sung cho bảng từ khóa của bill, vì chat dùng từ chung chung
("ăn", "nhậu", "chợ", "áo") mà bill hiếm khi có. Đã bỏ dấu, chữ thường; khớp nguyên từ, từ dài nhất thắng.
Bản mặc định nằm ở data/chat_keywords.json. Khi có DB, danh sách này được nạp từ bảng bill_keywords (kind = 'chat') thay cho bản mặc định (xem knowledge_pg.py)."""
from classification import data_files


# Khóa là mã danh mục chi tiêu (dining, transport...) hoặc mã loại thu nhập (salary, bonus, gift, side_income, refund, other_income).
CHAT_KEYWORDS = data_files.keyword_table("chat_keywords.json")
