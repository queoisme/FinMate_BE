# FinMate Backend — ASP.NET Core 9

Layered architecture (API → Application → Domain → Infrastructure), CQRS thủ công (không dùng MediatR).
Chi tiết đầy đủ: xem `../.context/ARCHITECTURE.md` §2, `../.context/TECH_STACK.md` §1, `../.context/CONVENTIONS.md`.

## Projects

| Project | Vai trò |
|---|---|
| `FinMate.API` | Entry point, Controllers, Middleware |
| `FinMate.Application` | Business logic, Commands/Queries (CQRS thủ công, không dùng MediatR) |
| `FinMate.Domain` | Entities, Enums, Value Objects — không phụ thuộc tầng nào khác |
| `FinMate.Infrastructure` | EF Core, Repositories, External services, Caching, Background jobs |
| `FinMate.Tests` | Unit + Integration tests |

## Trạng thái

Đã hoàn thành **Phase 0–4** (87/176 task, xem `../.context/TASKS.md`):

- **Phase 0** — scaffold solution, Serilog, Hangfire, Swagger, Rate Limiting, EF Core + PostgreSQL.
- **Phase 1** — Auth & User Profile: register/login/refresh-rotation/logout/change-password/delete-account, **Google Sign-In login** (`POST /auth/google`, auto-link tài khoản trùng email), profile + notification preferences.
- **Phase 2** — Financial Accounts: CRUD tài khoản ngân hàng/ví điện tử/tiền mặt, seed 5 provider (MB Bank, Vietcombank, MoMo, ZaloPay, VNPay).
- **Phase 3** — Categories: system categories (seed 11, slug khớp AI Service taxonomy) + custom category của user.
- **Phase 4** — Notifications & Transactions: `POST /notifications/analyze` (gọi AI Service qua `IAIServiceClient`/Refit, dedup theo hash, tạo transaction draft), transaction CRUD + confirm + cursor pagination. Cascade balance tài khoản đã làm đầy đủ; cascade budget/EXP/streak/mission đánh dấu `[!] Blocked by Phase 5/7` (2 module đó chưa tồn tại).

Phase 5 trở đi (Budget, Saving Goals, Reports, Gamification, Admin, AI Service) chưa bắt đầu. AI Service (`ai-service/`) vẫn chỉ là FastAPI scaffold trống — `/api/v1/analyze` thật chưa tồn tại, backend xử lý việc đó bằng `503 NOTIFICATION_AI_SERVICE_UNAVAILABLE` thay vì crash.

## API endpoints hiện có

```
POST   /api/v1/auth/register
POST   /api/v1/auth/login
POST   /api/v1/auth/google              # Google Sign-In (Android ID token)
POST   /api/v1/auth/refresh
POST   /api/v1/auth/logout
POST   /api/v1/auth/logout-all
POST   /api/v1/auth/change-password
DELETE /api/v1/auth/account

GET    /api/v1/users/me
PATCH  /api/v1/users/me
PATCH  /api/v1/users/me/notification-prefs

GET    /api/v1/financial-accounts
POST   /api/v1/financial-accounts
PATCH  /api/v1/financial-accounts/{id}
PATCH  /api/v1/financial-accounts/{id}/monitoring
GET    /api/v1/financial-accounts/{id}/balance
DELETE /api/v1/financial-accounts/{id}

GET    /api/v1/categories
POST   /api/v1/categories
PATCH  /api/v1/categories/{id}
DELETE /api/v1/categories/{id}

POST   /api/v1/notifications/analyze    # Android gửi notification đọc được, AI phân tích, tạo transaction draft nếu financial

GET    /api/v1/transactions             # Filter theo accountId/categoryId/type/fromDate/toDate, cursor pagination
GET    /api/v1/transactions/{id}
POST   /api/v1/transactions             # Tạo manual transaction (Status=Confirmed ngay)
PATCH  /api/v1/transactions/{id}
DELETE /api/v1/transactions/{id}
POST   /api/v1/transactions/{id}/confirm  # Xác nhận transaction draft (từ notification)
POST   /api/v1/transactions/parse       # Parse câu tiếng Việt tự nhiên qua AI Service, không persist

GET    /health                          # AllowAnonymous, dùng cho healthcheck
GET    /hangfire                        # Dashboard, basic auth (HANGFIRE_DASHBOARD_USER/PASS)
```

## Setup local

```bash
cp .env.example .env
```

Sửa `.env`:
- `JWT_SECRET` — chuỗi ngẫu nhiên ≥ 64 ký tự (bắt buộc, app từ chối khởi động nếu ngắn hơn).
- `GOOGLE_CLIENT_ID` — **Web OAuth Client ID** (không phải Android Client ID, không phải API Key) tạo trên Google Cloud Console → APIs & Services → Credentials. Bắt buộc để app khởi động (dù chưa dùng tính năng Google login).
- `AI_SERVICE_URL`/`AI_SERVICE_API_KEY` — bắt buộc để app khởi động. AI Service (`ai-service/`) hiện chỉ là scaffold trống (Phase 9 chưa code) nên request thật tới `/notifications/analyze` sẽ trả `503 NOTIFICATION_AI_SERVICE_UNAVAILABLE` cho tới khi Phase 9 xong — đây là hành vi đã thiết kế, không phải lỗi.
- `ADMIN_SEED_EMAIL`/`ADMIN_SEED_PASSWORD` — tài khoản admin mặc định, seed tự động khi migrate.

Chạy toàn bộ hạ tầng (từ thư mục gốc repo):

```bash
docker compose up -d postgres-main postgres-ai redis
docker compose up -d backend
curl http://localhost:8080/health
```

Backend tự chạy `Database.Migrate()` + seed admin user + seed provider configs khi khởi động — không cần chạy migration thủ công cho local dev.

## Commands

```bash
dotnet build
dotnet test
dotnet test --filter FullyQualifiedName~ClassName.MethodName   # 1 test cụ thể
dotnet ef migrations add <PascalCaseDescriptiveName> --project FinMate.Infrastructure --startup-project FinMate.API
```

**Lưu ý môi trường dev hiện tại:** host chỉ có .NET 10 runtime, không cài được .NET 9 runtime hệ thống (không có sudo) — mọi lệnh `dotnet build/test/ef` phải chạy qua container `mcr.microsoft.com/dotnet/sdk:9.0` (mount `backend/` + Docker socket cho Testcontainers). Xem lịch sử commit để biết câu lệnh `docker run` cụ thể.

`dotnet test` cần Docker chạy được (Testcontainers.PostgreSql dựng DB thật cho integration test). Tests Google login dùng `FakeGoogleTokenVerifier`, tests Notification/Transaction dùng `FakeAIServiceClient` (cả hai swap qua DI trong `AuthApiFactory`) thay vì gọi Google/AI Service thật.
