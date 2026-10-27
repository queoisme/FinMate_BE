"""Quy đổi nhóm của bộ luật bill/chat (9 nhóm) sang slug hệ thống của backend (CATEGORY_SLUGS).

Dùng chung cho OCR và chat để hai luồng xếp cùng một khoản vào cùng một danh mục. Slug lạ làm giao dịch
mất danh mục một cách im lặng, nên bảng chỉ chứa slug hợp lệ (có test).
Bảng nằm ở ``ocr-service/data/category_groups.json`` (khóa ``backend_slug``), cùng tệp với danh sách 9 nhóm mà OCR và DB dùng.
"""
from app.bill import lexicon

# ponytail: "groceries" (siêu thị) quy về "food" vì backend không có nhóm siêu thị riêng; đổi ở đây nếu muốn "shopping".
SLUG_BY_LOCAL_CATEGORY: dict[str, str] = lexicon.load("category_groups.json")["backend_slug"]

# Thu nhập của chat có loại con (lương, thưởng...), còn backend chỉ có một slug "income".
INCOME_SLUG = "income"

# Cho vay / mượn / trả nợ: danh mục hệ thống của backend nhưng KHÔNG thuộc CATEGORY_SLUGS, vì tập đó nằm trong các ràng buộc CHECK của DB AI
# (bill, user_feedback) và khoản này không bao giờ đi qua bill hay thông báo ngân hàng. Chỉ chat đặt slug này, cho cả hai chiều tiền.
LOAN_SLUG = "loan"
