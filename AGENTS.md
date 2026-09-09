# AGENTS.md — FinMate Project Configuration

> File này dành cho Claude Code, OpenCode và các CLI agents khác.
> Đọc file này TRƯỚC KHI làm bất cứ điều gì trong project.

---

## 1. Project Overview

**FinMate** là ứng dụng Android-first theo dõi tài chính cá nhân tự động bằng AI.
- Android app đọc notification từ ngân hàng/ví điện tử
- AI pipeline phân loại, trích xuất, phân danh mục giao dịch
- User xác nhận qua Mascot — gamification để duy trì thói quen

**Repo structure:**
```
finmate/
├── backend/          # ASP.NET Core 9 Web API
├── ai-service/       # Python 3.11 FastAPI
├── android/          # Kotlin Android app (không trong scope agent)
├── .context/         # Bối cảnh cho agents — ĐỌC TRƯỚC
│   ├── ARCHITECTURE.md
│   ├── TECH_STACK.md
│   ├── CONVENTIONS.md
│   └── TASKS.md
└── AGENTS.md         # File này
```

---

## 2. Mandatory Reading Order

Trước khi bắt đầu bất kỳ task nào, agent PHẢI đọc theo thứ tự:

```
1. AGENTS.md               (file này)
2. .context/ARCHITECTURE.md
3. .context/TECH_STACK.md
4. .context/CONVENTIONS.md
5. .context/TASKS.md       (chỉ đọc phần task đang làm)
```

Không được bỏ qua bước nào. Nếu thiếu context → hỏi trước, không tự đoán.

---

## 3. Hard Rules — Không bao giờ vi phạm

### 3.1 Kiến trúc
- KHÔNG để business logic trong Controller — chỉ validate input và gọi Service
- KHÔNG truy cập DB trực tiếp từ Controller hay AI Service handler
- KHÔNG để raw SQL trong Service layer — dùng Repository pattern
- KHÔNG share database giữa Backend và AI Service
- KHÔNG lưu `user_id` thực trong AI DB — luôn dùng `SHA-256(user_id)`

### 3.2 Bảo mật
- KHÔNG log password, token, `amount_cents`, `notification_body` gốc
- KHÔNG trả stack trace về client trong production response
- KHÔNG lưu `refresh_token` raw vào DB — luôn lưu SHA-256 hash
- KHÔNG bỏ qua `[Authorize]` attribute trên bất kỳ endpoint nào trừ `/auth/*`
- KHÔNG lưu số tiền dạng `decimal` hay `float` — dùng `BIGINT` (đơn vị: đồng VND)

### 3.3 Data
- KHÔNG dùng `DateTime` — luôn dùng `DateTimeOffset` trong C#
- KHÔNG dùng `int` cho ID — luôn dùng `Guid`
- KHÔNG xóa record vật lý trừ khi có explicit hard delete requirement
- KHÔNG tính balance real-time từ tất cả transactions — dùng pre-computed aggregates

### 3.4 Code quality
- KHÔNG commit code có warning chưa xử lý
- KHÔNG dùng `var` cho kiểu không rõ ràng
- KHÔNG để method dài hơn 50 dòng — tách thành private methods
- KHÔNG magic string — dùng `const` hoặc `enum`

---

## 4. Scope của Agent

### Được làm (trong scope)
- Tạo/sửa file trong `backend/` và `ai-service/`
- Tạo EF Core migrations
- Viết unit tests và integration tests
- Cập nhật `.context/TASKS.md` sau khi hoàn thành task
- Tạo file mới nếu đúng convention

### Không được làm (ngoài scope)
- Sửa file trong `android/` — đây là scope riêng của mobile team
- Xóa migration đã apply vào DB
- Thay đổi schema bảng mà không tạo migration mới
- Push trực tiếp lên branch `main` hay `develop`
- Thay đổi `.context/ARCHITECTURE.md` hay `.context/TECH_STACK.md` mà không có approval

---

## 5. Khi không chắc

Ưu tiên theo thứ tự:
1. Đọc lại `.context/ARCHITECTURE.md` — câu trả lời thường ở đó
2. Đọc code hiện có cùng pattern trong project
3. Hỏi người dùng — mô tả rõ vấn đề và đưa ra 2-3 options

**Không được tự ý quyết định khi:**
- Thay đổi database schema
- Thêm dependency mới chưa có trong `TECH_STACK.md`
- Thay đổi API contract giữa Backend và AI Service
- Xử lý edge case liên quan đến tiền tệ hay bảo mật

---

## 6. Sau khi hoàn thành task

1. Chạy lại toàn bộ tests liên quan
2. Kiểm tra không có warning mới
3. Cập nhật `.context/TASKS.md` — đánh dấu Done và ghi note ngắn
4. Tóm tắt những gì đã làm và file nào đã thay đổi
