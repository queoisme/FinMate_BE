# FinMate

Ứng dụng Android-first theo dõi tài chính cá nhân tự động bằng AI — đọc notification ngân hàng/ví điện tử, phân loại giao dịch bằng AI pipeline, xác nhận qua Mascot gamification.

## Bắt đầu ở đây

1. **[AGENTS.md](AGENTS.md)** — đọc TRƯỚC KHI làm bất cứ điều gì (dành cho agent CLI, nhưng con người cũng nên đọc)
2. **[.context/ARCHITECTURE.md](.context/ARCHITECTURE.md)** — kiến trúc hệ thống, layer boundaries, DB schema, API contract Backend ↔ AI Service
3. **[.context/TECH_STACK.md](.context/TECH_STACK.md)** — công nghệ, package được/không được dùng
4. **[.context/CONVENTIONS.md](.context/CONVENTIONS.md)** — API design, error handling, validation, logging, security conventions
5. **[.context/TASKS.md](.context/TASKS.md)** — task board theo phase, cập nhật sau mỗi task hoàn thành

## Repo structure

```
finmate/
├── backend/          # ASP.NET Core 9 Web API — xem backend/README.md
├── ai-service/       # Python 3.11 FastAPI — xem ai-service/README.md
├── android/          # Kotlin Android app (không trong scope agent — repo/scope riêng)
├── .context/         # Bối cảnh cho agents
└── AGENTS.md
```

## Trạng thái

Giai đoạn cấu trúc dự án (Phase 0 chưa bắt đầu) — chưa có code, chỉ có skeleton thư mục + tài liệu. Xem `.context/TASKS.md` để biết progress: **0 / 176 tasks**.
