Controllers dành riêng cho Admin, dùng policy `AdminOnly` (xem .context/CONVENTIONS.md §6.1).
`AdminAiRulesController` (`/api/v1/admin/ai-rules`) chuyển tiếp nguyên văn tới API quản trị luật của AI Service; xem `docs/ai-rules-admin-api.md`.
`AdminAiConfigController` (`/api/v1/admin/ai-config`) chuyển tiếp nguyên văn tới API quản trị tài liệu cấu hình có phiên bản của AI Service (từ vựng chat, từ điển, ngưỡng và chữ nhận dạng màn hình app của OCR): nháp, xem trước, kích hoạt, hoàn tác; xem `docs/ai-config-admin-api.md`.
