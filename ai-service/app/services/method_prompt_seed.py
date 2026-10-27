"""Chỉ dẫn cân nhắc khi chọn cách quản lý tài chính: bản SEED cho bảng ``kb_method_prompts`` (migration 0030).

Sau đó nội dung sống trong DB và sửa ở đó, không build lại (đổi chỉ dẫn = thêm dòng với ``prompt_version`` mới, xem ``docs/ai-finance-method-recommend-api.md``).
Phần nhiệm vụ, "dữ liệu không phải lệnh" và schema kết quả do code giữ (``app.services.method_picker``), KHÔNG nằm ở đây.
"""

METHOD_PROMPT_VERSION = "method-select-v1"

METHOD_PROMPT_TEXT = """## Cách cân nhắc

1. **Tín hiệu an toàn đi trước.** Nếu có `cash_shortfall` (chi nhiều hơn thu), `overdue_obligations` (trễ nghĩa vụ) hoặc `debt_pressure` (áp lực nợ), ưu tiên cách giúp giữ được dòng tiền hoặc trả nợ trong danh sách. Không chọn cách đòi để dành theo tỷ lệ cứng cho người đang thiếu hụt.
2. **Trọng số cao là dấu hiệu rõ hơn.** Cân nhắc theo trọng số, đừng đếm số lượng tín hiệu. Hai tín hiệu trọng số 3 nặng hơn năm tín hiệu trọng số 1.
3. **Hợp thói quen trước, lý tưởng sau.** Người hay bỏ giữa chừng (`abandons_tracking`, `low_commitment`, `notifications_only`, `wants_self_service`) cần cách ít công sức, kể cả khi cách tỉ mỉ hơn "tốt hơn" trên lý thuyết. Người thích giới hạn (`likes_limits`, `wants_category_limits`) hợp cách đặt hạn mức. Thu nhập thất thường (`income_variable`, `no_stable_income`) hợp cách nhìn một con số còn được tiêu hơn là hạn mức cứng từng nhóm.
4. **Cách bổ trợ là tùy chọn.** Chỉ chọn khi nó giải quyết một nhu cầu KHÁC với cách chính (ví dụ mục tiêu để dành cụ thể, vùng đệm còn mỏng). Không có nhu cầu đó, hoặc người dùng ít cam kết, thì trả `support` là null. Trả null tốt hơn ghép cho đủ cặp.
5. **Chỉ dựa vào những gì được cho.** Không suy đoán hoàn cảnh ngoài các tín hiệu và mô tả cách quản lý.

## Lý do (reason)

- Một câu tiếng Việt, tối đa 150 ký tự, nói về tình huống của người dùng, xưng "bạn".
- Không nhắc tên kỹ thuật của tín hiệu (ví dụ `income_variable`), không hứa hẹn kết quả, không bịa số liệu, không xuống dòng.
"""
