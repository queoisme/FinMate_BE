"""Quy đổi nhóm của bộ luật bill/chat (9 nhóm) sang slug hệ thống của backend (CATEGORY_SLUGS).

Dùng chung cho OCR và chat để hai luồng xếp cùng một khoản vào cùng một danh mục. Slug lạ làm giao dịch
mất danh mục một cách im lặng, nên bảng chỉ chứa slug hợp lệ (có test).
"""

# ponytail: "groceries" (siêu thị) quy về "food" vì backend không có nhóm siêu thị riêng; đổi ở đây nếu muốn "shopping".
SLUG_BY_LOCAL_CATEGORY: dict[str, str] = {
    "dining": "food",
    "groceries": "food",
    "shopping": "shopping",
    "transport": "transport",
    "housing": "housing",
    "utilities": "bills",
    "healthcare": "health",
    "entertainment": "entertainment",
    "other": "other",
}

# Thu nhập của chat có loại con (lương, thưởng...), còn backend chỉ có một slug "income".
INCOME_SLUG = "income"
