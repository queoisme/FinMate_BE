# TASKS.md — FinMate Task Board

> Cập nhật file này sau mỗi task hoàn thành.
> Format: `- [x] Task name — *Note ngắn nếu có*`

---

## Legend

```
[ ] Todo       — chưa bắt đầu
[~] In Progress — đang làm
[x] Done       — hoàn thành
[!] Blocked    — bị block bởi task khác (ghi rõ blocked by gì)
[-] Skipped    — bỏ qua có lý do (ghi rõ lý do)
```

---

## Phase 0 — Project Setup

### Infrastructure

- [x] Tạo `docker-compose.yml` với đủ services (backend, ai-service, postgres-main, postgres-ai, redis) — *Hangfire dashboard phục vụ qua path `/hangfire` trên chính service backend, không phải container riêng (xem note trong docker-compose.yml). Đã verify `docker compose up` full stack, cả 5 container healthy/running.*
- [x] Tạo `.env.example` cho backend và ai-service
- [x] Cấu hình Serilog structured JSON logging cho backend
- [x] Cấu hình structlog cho ai-service
- [x] Setup `EditorConfig` và `.gitignore`

### Backend Project Init

- [x] Tạo solution `FinMate.sln` với 4 projects (API, Application, Domain, Infrastructure, Tests) — *Dựng đủ 5 project theo ARCHITECTURE.md §2.1 (typo trong task gốc: liệt kê 5 tên nhưng ghi "4 projects").*
- [x] Cấu hình `nullable enable`, `implicit usings` toàn solution — *qua `Directory.Build.props`, kèm `TreatWarningsAsErrors=true`.*
- [x] Cấu hình Npgsql với `UseSnakeCaseNamingConvention()` — *Dùng `HasColumnName()` tường minh từng property thay vì thêm package `EFCore.NamingConventions` (không có trong TECH_STACK.md, tránh thêm dependency chưa duyệt).*
- [x] Cấu hình `ExceptionHandlingMiddleware`
- [x] Cấu hình FluentValidation auto-registration
- [x] Cấu hình AutoMapper profile scan
- [x] Cấu hình Hangfire với PostgreSQL storage
- [x] Cấu hình Swagger/OpenAPI với JWT auth support
- [x] Cấu hình Rate Limiting (ASP.NET Core built-in)

### AI Service Project Init

- [x] Tạo FastAPI project structure theo `ARCHITECTURE.md`
- [x] Setup Alembic cho AI DB migrations — *env.py dùng async engine, trỏ `finmate_ai`.*
- [x] Cấu hình structlog
- [x] Cấu hình internal API key authentication middleware
- [x] Setup `pytest` với `pytest-asyncio` — *cấu hình trong `pyproject.toml`; chưa có test case thật (pipeline logic thuộc Phase 9).*
- [x] Chạy `pytest`/`black`/`ruff`/`isort` trực tiếp trên host qua pyenv Python 3.11.16 — *Đã cài đủ lib hệ thống (`libffi-devel`/`readline-devel`/`sqlite-devel`/`xz-devel`/`tk-devel`/`bzip2-devel`; `zlib-devel` không còn tồn tại trên Fedora 44, thay bằng `zlib-ng-compat-devel` đã có sẵn), rebuild Python 3.11.16, tạo `.venv`, cài `requirements.txt` + `black`/`ruff`/`isort`. Verified: `pytest` chạy được (0 test), `black --check`/`ruff check`/`isort --check-only` đều pass.*

---

## Phase 1 — Domain 1 & 2: Auth & User Profile

### Database

- [x] Migration: tạo bảng `users`
- [x] Migration: tạo bảng `refresh_tokens`
- [x] Migration: tạo bảng `audit_logs`
- [x] Migration: tạo bảng `data_deletion_requests` — *Cả 4 bảng gộp trong 1 migration `CreateAuthTables` (EF snapshot-diff không tách được thành 4 migration riêng có ý nghĩa do tạo cùng lúc — tách sẽ ra 3 migration rỗng).*
- [x] Seed: tạo admin user mặc định — *`AdminUserSeeder`, đọc `ADMIN_SEED_EMAIL`/`ADMIN_SEED_PASSWORD` từ env, idempotent, chạy sau `Database.Migrate()`. Verified: login thành công qua API.*

### Backend — Auth Module

- [x] Entity: `User`, `RefreshToken`, `AuditLog` — *+ `DataDeletionRequest` (cần cho DeleteAccount/DataDeletionJob).*
- [x] Repository: `IUserRepository`, `IRefreshTokenRepository` — *+ `IDataDeletionRequestRepository`, `IUserHardDeleter`.*
- [x] Command: `RegisterCommand` + Handler + Validator
- [x] Command: `LoginCommand` + Handler + Validator
- [x] Command: `RefreshTokenCommand` + Handler — *Rotation + reuse-detection (revoke-all khi phát hiện token cũ bị dùng lại), atomic qua `RotateAsync` (1 DB transaction).*
- [x] Command: `LogoutCommand` + Handler
- [x] Command: `LogoutAllDevicesCommand` + Handler
- [x] Command: `ChangePasswordCommand` + Handler + Validator — *Revoke toàn bộ refresh token hiện có sau khi đổi mật khẩu.*
- [x] Command: `DeleteAccountCommand` + Handler — *Yêu cầu xác nhận password; soft-delete User + tạo `DataDeletionRequest` (+30 ngày).*
- [x] Command: `GoogleLoginCommand` + Handler + Validator — *Kéo ra khỏi Backlog theo yêu cầu trực tiếp của user (2026-09-10), không theo thứ tự phase gốc. `POST /api/v1/auth/google` nhận Google ID token (Android Sign-In flow), verify qua `Google.Apis.Auth`. Auto-link nếu email trùng tài khoản password đã có; tạo user mới (`PasswordHash=null`) nếu chưa tồn tại. `users.password_hash` đổi thành nullable + thêm `google_id` (unique partial index) — migration `AddGoogleLoginToUsers`. Đã guard 3 chỗ có thể crash khi `PasswordHash=null` (Login/ChangePassword/DeleteAccount).*
- [x] Controller: `AuthController` với tất cả endpoints
- [x] Middleware: JWT authentication cấu hình — *Global `[Authorize]` fallback policy, `[AllowAnonymous]` cho register/login/refresh; lưu ý fallback policy cũng áp dụng cho route không khớp endpoint nào — `/health` cần `.AllowAnonymous()` tường minh.*
- [x] Service: `TokenService` (generate, validate, hash JWT/refresh token)
- [x] Service: `AuditLogService` (ghi audit events)
- [x] Job: `DataDeletionJob` (hard delete sau 30 ngày) — *Hangfire recurring, daily 03:00 (không có trong bảng job ARCHITECTURE.md §5, chọn cùng slot ý nghĩa với DataCleanupJob).*

### Backend — User Profile Module

- [x] Query: `GetUserProfileQuery` + Handler — *Cache-aside Redis `user:{id}:profile`, TTL 15 phút.*
- [x] Command: `UpdateUserProfileCommand` + Handler + Validator
- [x] Command: `UpdateNotificationPrefsCommand` + Handler — *3 boolean: pushEnabled, budgetAlertsEnabled, missionRemindersEnabled.*
- [x] Controller: `UsersController`

### Tests

- [x] Unit: `RegisterCommandHandler` tests (email duplicate, password validation)
- [x] Unit: `LoginCommandHandler` tests (wrong password, locked account)
- [x] Unit: `RefreshTokenCommandHandler` tests (rotation, reuse detection)
- [x] Integration: `AuthController` endpoints — *WebApplicationFactory + Testcontainers.PostgreSql, full round-trip register→login→refresh→reuse-detection. Redis thay bằng MemoryDistributedCache trong test (không có Redis thật trong môi trường test).*

**Verify Phase 0+1 (2026-09-09):** `dotnet build` sạch 0 warning; `dotnet test` xanh 10/10 (chạy qua container SDK 9.0 vì host chỉ có .NET 10 runtime, không có sudo để cài .NET 9 runtime hệ thống); `docker compose up` — 5 container healthy/running; smoke test curl end-to-end (register/login/refresh-rotation/reuse-detection/admin-seed-login) đều đúng như thiết kế. Một bug thật được tìm thấy và sửa qua smoke test: enum `HasConversion<string>()` ghi PascalCase trong khi check constraint DB kỳ vọng lowercase.

**Bổ sung Google login (2026-09-10, theo yêu cầu trực tiếp của user, kéo ra khỏi Backlog):** `POST /api/v1/auth/google` — verify ID token qua `Google.Apis.Auth` (`GoogleJsonWebSignature.ValidateAsync`), auto-link tài khoản trùng email, tạo user mới nếu chưa có (`PasswordHash=null`). `dotnet build` sạch; `dotnet test` xanh 51/51; `dotnet ef migrations has-pending-model-changes` sạch; `docker compose up` — `password_hash` nullable + `google_id` partial unique index áp dụng đúng trên DB đã có data cũ; curl với token rác → `401 AUTH_TOKEN_INVALID` (chứng minh pipeline verify chạy tới Google thật), idToken rỗng → `400 VALIDATION_ERROR`; toàn bộ luồng password cũ (register/login/change-password) verify lại vẫn hoạt động đúng sau migration. **Giới hạn:** không test được nhánh thành công qua curl thật (không có Google OAuth client/internet ra ngoài trong môi trường này) — nhánh thành công (user mới/auto-link/existing-GoogleId/account-locked) được cover đầy đủ qua integration test dùng `FakeGoogleTokenVerifier` (swap `IGoogleTokenVerifier` trong `AuthApiFactory`) thay vì gọi Google thật.

**Retro-fix (2026-09-10, phát hiện khi test kỹ Phase 2):** `AddFluentValidationAutoValidation()` chỉ auto-validate action parameter (Request DTO), không validate Command được build thủ công trong controller — nghĩa là `RegisterCommandValidator`/`LoginCommandValidator`/`ChangePasswordCommandValidator`/`UpdateUserProfileCommandValidator` (đăng ký ở Phase 1) **chưa từng thực sự chạy**: `/auth/register` từng chấp nhận password 3 ký tự và email sai định dạng. Fix: inject `IValidator<TCommand>` vào từng Handler, gọi `ValidateAndThrowAsync` đầu `HandleAsync` (thay thế tương đương cho MediatR validation pipeline behavior mà kiến trúc CQRS thủ công không có sẵn). Áp dụng cùng lúc cho 2 validator mới ở Phase 2. Đã verify lại qua `dotnet test` + curl trên `docker compose` thật.

---

## Phase 2 — Domain 3: Financial Accounts

### Database

- [x] Migration: tạo bảng `provider_configs`
- [x] Migration: tạo bảng `financial_accounts` — *Cả 2 bảng gộp trong 1 migration `CreateFinancialAccountTables` (tạo cùng lúc, giống quyết định ở Phase 1). Unique partial index `uq_financial_accounts_user_package` trên `(user_id, package_name) WHERE package_name IS NOT NULL AND deleted_at IS NULL` enforce "duplicate package_name per user" ở tầng DB.*
- [x] Seed: system provider configs (MB Bank, Vietcombank, MoMo, ZaloPay, VNPay) — *`ProviderConfigSeeder`, idempotent (upsert theo `provider_key`), chạy sau `AdminUserSeeder` trong `Program.cs`. Verified qua `psql`: đủ 5 dòng, đúng package_name.*

### Backend

- [x] Entity: `FinancialAccount`, `ProviderConfig`
- [x] Repository: `IFinancialAccountRepository` — *+ `IProviderConfigRepository` (đọc tối thiểu, dùng để validate provider khi tạo account; CRUD đầy đủ thuộc Phase 8 `AdminProviderConfigsController`).*
- [x] Command: `CreateFinancialAccountCommand` + Handler + Validator
- [x] Command: `UpdateFinancialAccountCommand` + Handler
- [x] Command: `ToggleAccountMonitoringCommand` + Handler
- [x] Command: `DeleteFinancialAccountCommand` + Handler (soft)
- [x] Query: `GetAccountListQuery` + Handler
- [x] Query: `GetAccountBalanceQuery` + Handler (tính từ transactions) — *`Transaction` entity chưa tồn tại (Phase 4) nên trả về `balance_cents` — pre-computed aggregate lưu sẵn trên `financial_accounts` (đúng AGENTS.md §3.3, cấm tính real-time từ transactions). Phase 4 sẽ cộng/trừ giá trị này trong `ConfirmTransactionCommand`/`DeleteTransactionCommand`.*
- [x] Controller: `FinancialAccountsController`

### Tests

- [x] Unit: duplicate package_name per user validation — *`CreateFinancialAccountCommandHandlerTests` + integration test `CreateBankAccount_DuplicatePackageNameForSameUser_ReturnsConflict` (409 qua HTTP thật, dùng provider seed thật).*
- [x] Unit: không xóa account đang có transactions — *Bổ sung ở Phase 4 khi `ITransactionRepository` được dựng, xem TransactionsControllerTests/DeleteFinancialAccountCommandHandlerTests. 409 `FINANCIAL_ACCOUNT_HAS_TRANSACTIONS`.*

---

## Phase 3 — Domain 6: Categories

### Database

- [x] Migration: tạo bảng `categories` — *Nullable `user_id` phân biệt system (`NULL`) vs user custom category. 2 partial unique index tách riêng: `uq_categories_system_slug` (`user_id IS NULL`) và `uq_categories_user_slug` (`user_id IS NOT NULL`).*
- [x] Seed: 11 system categories (food, transport, shopping, education, housing, bills, entertainment, health, family, income, other) — *`CategorySeeder`, slug khớp đúng taxonomy `category_slug` mà AI Service trả về (ARCHITECTURE.md §3.3).*

### Backend

- [x] Entity: `Category`
- [x] Repository: `ICategoryRepository`
- [x] Query: `GetCategoryListQuery` (system + user custom)
- [x] Command: `CreateCategoryCommand` + Handler + Validator — *Slug tự sinh server-side từ `Name` qua bảng ánh xạ ký tự tiếng Việt tường minh (`Slugify`), không dùng `string.Normalize`/`CharUnicodeInfo` vì solution bật `InvariantGlobalization=true` khiến 2 API đó cho kết quả sai (bug thật phát hiện qua test, xem note Retro-fix bên dưới).*
- [x] Command: `UpdateCategoryCommand` + Handler — *Chỉ sửa `Name`/`IconName`, không đổi `Slug`. Repository chỉ trả category do chính user sở hữu (`GetOwnedByUserAsync`) nên sửa/xóa category hệ thống hoặc của người khác đều trả 404, không lộ tồn tại.*
- [x] Command: `DeleteCategoryCommand` + Handler (check có transaction không) — *Guard `CATEGORY_HAS_TRANSACTIONS` (409) thêm trong commit dựng Transaction module (Phase 4), cùng lượt nên không bị block như FinancialAccount ở Phase 2.*
- [x] Controller: `CategoriesController`

---

## Phase 4 — Domain 4 & 5: Notifications & Transactions (Core)

### Database

- [x] Migration: tạo bảng `notification_logs`
- [x] Migration: tạo bảng `ai_results`
- [x] Migration: tạo bảng `saving_goals` (trước transactions vì FK) — *Bảng tối thiểu (Id/UserId/Name/Target/SavedCents/Status/Deadline) chỉ để `transactions.saving_goal_id` có FK trỏ tới — không có Repository/Command/Query/Controller, giống pattern `provider_configs` ở Phase 2. Full CRUD thuộc Phase 5.*
- [x] Migration: tạo bảng `transactions` — *4 bảng gộp 1 migration `CreateTransactionTables` (tạo cùng lúc, giống Phase 1/2). Không có FK sang `budgets` (chưa tồn tại, cascade budget là logic tầng Application, không phải schema — xem note Blocked bên dưới).*

### Backend — Notification Module

- [x] Entity: `NotificationLog`, `AiResult`
- [x] Repository: `INotificationLogRepository`
- [x] Service: `IAIServiceClient` interface + `AIServiceClient` implementation (Refit) — *Băm SHA-256 `user_id`/`transaction_id` ở tầng Infrastructure trước khi gửi qua HTTP (AGENTS.md §3.1). Lỗi HTTP/timeout → `AIServiceUnavailableException` (503 `NOTIFICATION_AI_SERVICE_UNAVAILABLE`) thay vì crash.*
- [x] Command: `AnalyzeNotificationCommand` + Handler
  - [x] Validate package trong whitelist user — *Chỉ xử lý nếu package khớp `FinancialAccount.PackageName` đang `IsMonitored=true` của user; không khớp → `NotificationLog.Status=Ignored`, không lỗi.*
  - [x] Hash dedup check — *SHA-256(package+title+body+received_at làm tròn phút), cửa sổ 5 phút.*
  - [x] Gọi AI Service
  - [x] Lưu notification_log và ai_result
  - [x] Tạo transaction draft nếu financial
  - [x] Push notification về Android — *Chưa có push provider (FCM) được duyệt trong `TECH_STACK.md` → `LoggingPushNotificationService` (log-only stub qua `IPushNotificationService`). Cần quyết định provider trước khi làm bản thật.*
- [x] Controller: `NotificationsController`
- [x] Job: `RetryFailedNotificationJob` (retry status='failed', retry_count < 3) — *Hangfire, mỗi 15 phút (ARCHITECTURE.md §5).*

### Backend — Transaction Module

- [x] Entity: `Transaction`
- [x] Repository: `ITransactionRepository` — *`GetListAsync` dùng keyset/cursor pagination trên `(TransactedAt, CreatedAt)` — không dùng `Id` vì so sánh `Guid` bằng `<`/`>` không dịch được sang SQL đáng tin cậy qua EF/Npgsql.*
- [x] Command: `ConfirmTransactionCommand` + Handler
  - [x] Validate ownership
  - [x] DB transaction cho cascade: budget + gamification + streak — *Atomicity đạt được không cần thêm abstraction Unit-of-Work: `FinancialAccountRepository` và `TransactionRepository` dùng chung 1 scoped `DbContext`/request, nên sửa cả 2 entity rồi gọi `SaveChangesAsync` 1 lần (qua handler nào cũng được) flush cả 2 trong 1 DB transaction ngầm của EF Core.*
  - [x] Budget period update — *Hoàn thành ở Phase 5 (2026-09-11): gọi `IBudgetPeriodService.ApplyDeltaAsync`.*
  - [!] EXP award — *Blocked by Phase 7: Gamification module chưa tồn tại.*
  - [!] Streak check — *Blocked by Phase 7.*
  - [!] Mission condition trigger — *Blocked by Phase 7.*
- [x] Command: `CreateManualTransactionCommand` + Handler + Validator — *Tạo trực tiếp `Status=Confirmed` (không qua bước confirm riêng vì không có AI draft), cascade balance ngay.*
- [x] Command: `UpdateTransactionCommand` + Handler + Validator
  - [x] Revert budget nếu category/amount/date thay đổi — *Hoàn thành ở Phase 5 (2026-09-11): revert theo category/số tiền/ngày CŨ trước khi ghi đè entity, rồi áp giá trị MỚI — cả ba đều có thể trỏ sang budget khác và chu kỳ khác.*
  - [x] Lưu AI correction nếu category thay đổi — *`POST /api/v1/feedback` best-effort, chỉ khi `Transaction.Source=Notification` (có category AI dự đoán để so sánh).*
- [x] Command: `DeleteTransactionCommand` + Handler
  - [x] Revert budget nếu đã confirmed — *Hoàn thành ở Phase 5 (2026-09-11).*
  - [!] Revert EXP — *Blocked by Phase 7.*
  - *(Revert `FinancialAccount.BalanceCents` khi xóa giao dịch đã Confirmed — không bị block, đã làm.)*
- [x] Command: `ParseNaturalLanguageCommand` + Handler (gọi AI Service) — *Tái dùng `POST /api/v1/analyze` với `package_name="manual_entry"` thay vì thêm route AI Service mới (đã hỏi user trước khi quyết định, theo AGENTS.md §5 — đổi API contract Backend↔AI Service cần approval). Không persist, chỉ trả field để client prefill form tạo manual transaction.*
- [x] Query: `GetTransactionListQuery` + Handler (filter, cursor pagination)
- [x] Query: `GetTransactionDetailQuery` + Handler
- [x] Controller: `TransactionsController`

### Tests — Critical

- [x] Unit: `ConfirmTransactionCommandHandler`
  - [x] Cascade balance update (debit/credit đúng chiều) — *thay cho "cascade budget update", xem note Blocked by Phase 5 ở trên.*
  - [-] EXP award — *Skipped: Blocked by Phase 7.*
  - [-] Streak update — *Skipped: Blocked by Phase 7.*
  - [x] Rollback khi lỗi — *test "đã confirmed rồi không confirm lại được" (422 `TRANSACTION_NOT_DRAFT`).*
- [x] Unit: `DeleteTransactionCommandHandler` — revert logic (balance; budget/EXP revert skipped, xem note Blocked)
- [x] Unit: `UpdateTransactionCommandHandler` — category change revert (feedback call) + amount/account change (balance revert+reapply)
- [x] Integration: Full notification → draft → confirm flow — *Dùng `FakeAIServiceClient` (swap DI trong `AuthApiFactory`, giống `FakeGoogleTokenVerifier`) vì AI Service (Phase 9) chưa có route thật.*

**Bổ sung guard bị Blocked ở Phase 2/3 (cùng lượt, vì `ITransactionRepository` giờ đã tồn tại):** `DeleteFinancialAccountCommandHandler` và `DeleteCategoryCommandHandler` giờ chặn xóa khi còn giao dịch tham chiếu (409 `FINANCIAL_ACCOUNT_HAS_TRANSACTIONS`/`CATEGORY_HAS_TRANSACTIONS`).

---

## Phase 5 — Domain 7 & 8: Budget & Saving Goals

**3 quyết định đã hỏi user trước khi làm (AGENTS.md §5 — schema/nghiệp vụ không tự quyết):**
1. Budget áp cho **cả per-category lẫn tổng chi tiêu** → `budgets.category_id` nullable (NULL = budget tổng). Một giao dịch có thể tiêu hạn mức của cả 2 budget cùng lúc.
2. `period_type` **chỉ `monthly`** — giữ cột enum để thêm weekly sau mà không phải đổi schema.
3. `POST /saving-goals/{id}/contribute` là **bookkeeping thuần**: chỉ ghi `goal_contributions` + cộng `saved_cents`, KHÔNG trừ `FinancialAccount.BalanceCents` và KHÔNG tạo `Transaction` (nếu tạo, tiền để dành sẽ vừa vào goal vừa ăn vào budget chi tiêu → tính trùng). Cột `transactions.saving_goal_id` từ Phase 4 giữ nguyên, chưa dùng.

### Database

- [x] Migration: tạo bảng `budgets` — *2 partial unique index (`uq_budgets_user_category` WHERE `category_id IS NOT NULL`, `uq_budgets_user_total` WHERE `category_id IS NULL`) vì Postgres coi mỗi NULL là distinct nên 1 index gộp không chặn được nhiều budget tổng — cùng pattern `CategoryConfiguration` ở Phase 3. Không có cột `is_active`: soft delete đã là "ngừng áp dụng", nên `BUDGET_CATEGORY_HAS_ACTIVE_BUDGET` (liệt kê trong `CONVENTIONS.md` §2.1) là thừa và không được dùng.*
- [x] Migration: tạo bảng `budget_periods` — *`limit_cents` là snapshot lúc tạo period để đổi hạn mức không làm sai lịch sử. Không soft delete (bảng con, giống `notification_logs`/`ai_results`).*
- [x] Migration: tạo bảng `goal_contributions` — *3 bảng mới + hoàn thiện `saving_goals` gộp 1 migration `CreateBudgetAndGoalTables`. Ledger bất biến, không có endpoint xóa → không có `deleted_at`.*

### Backend — Budget Module

- [x] Entity: `Budget`, `BudgetPeriod`
- [x] Repository: `IBudgetRepository`
- [x] Service: `IBudgetPeriodService` (incremental update logic) — *`ApplyDeltaAsync(userId, categoryId, spentDelta, transactedAt)`: delta dương = chi tiêu mới, âm = revert; chỉ `Debit` tiêu hạn mức (`Credit` là tiền vào). Duyệt mọi budget khớp (category + tổng), get-or-create period của chu kỳ chứa `transactedAt`, clamp `spent_cents` ở 0. **Không gọi `SaveChangesAsync`** — caller vẫn kết thúc bằng đúng 1 lần save nên transaction + balance + budget flush trong cùng 1 DB transaction ngầm của EF Core (đúng pattern atomicity đã ghi ở `ConfirmTransactionCommandHandler`).*
- [x] Biên chu kỳ (`BudgetCalendar`) — *Tính theo **UTC+7 cố định** thay vì `TimeZoneInfo.FindSystemTimeZoneById`: `InvariantGlobalization=true` bật solution-wide làm API phụ thuộc ICU không đáng tin (cùng lý do đã buộc viết lại `Slugify` ở Phase 3), và app chỉ phục vụ VN. Giao dịch lúc 18:00 UTC ngày cuối tháng thuộc về tháng sau theo giờ VN — có test chốt. **Mốc chu kỳ luôn trả về ở offset 0**: Npgsql từ chối ghi `DateTimeOffset` có offset khác 0 vào cột `timestamptz`, kể cả khi chỉ dùng làm tham số truy vấn (lỗi này làm mọi endpoint budget trả 500 lúc đầu). Hệ quả: (a) muốn năm/tháng để dựng cache key thì phải dùng `BudgetCalendar.VietnamYearMonth`, đọc thẳng `.Year`/`.Month` của giá trị UTC sẽ ra tháng trước; (b) `start.AddMonths(1)` không phải mốc cuối chu kỳ vì cộng tháng lên một mốc UTC lệch khi 2 tháng khác số ngày.*
- [x] Command: `CreateBudgetCommand` + Handler + Validator — *Tạo budget giữa tháng thì backfill `spent_cents` từ giao dịch đã Confirmed trong chu kỳ (`ITransactionRepository.SumConfirmedSpendAsync`), nếu không budget mới luôn hiện 0 dù user đã tiêu cả tháng.*
- [x] Command: `UpdateBudgetLimitCommand` + Handler — *Chỉ period đang chạy nhận limit mới; period quá khứ giữ snapshot. Nâng hạn mức qua ngưỡng thì reset cờ alert tương ứng để cảnh báo bắn lại được.*
- [x] Command: `DeleteBudgetCommand` + Handler — *Soft delete, giữ `budget_periods` làm lịch sử.*
- [x] Query: `GetBudgetSummaryQuery` (current month overview) — *Cache Redis 5 phút theo `ARCHITECTURE.md` §6 (`CacheKeys.BudgetSummary`), invalidate ở cả 3 command budget lẫn 4 handler transaction. Response tách `TotalBudget` (budget tổng, nullable) khỏi `CategoryBudgets` thay vì gộp một con số "tổng": budget tổng bao trùm mọi category nên mọi cách cộng chung đều hoặc tính trùng, hoặc cho ra field tên "total spent" nhưng không phải tổng chi tiêu — smoke test thật đã lộ đúng cái bẫy đó. `PeriodStart`/`PeriodEnd` trả về ở `+07:00` để client đọc "01/09 → 01/10", còn tầng lưu trữ vẫn dùng UTC.*
- [x] Controller: `BudgetsController`
- [x] Job: `BudgetAlertJob`
  - [x] Query budget_periods gần limit — *Join tường minh sang `_context.Budgets` thay vì `Include`: join áp query filter soft-delete (budget đã xóa ngừng cảnh báo) và mang theo `UserId` mà `budget_periods` không lưu.*
  - [x] Check `alert_80_sent_at` và `alert_100_sent_at` — *Mỗi ngưỡng bắn đúng 1 lần/chu kỳ. Chi tiêu có thể nhảy thẳng từ dưới 80% lên quá 100% giữa 2 lần chạy, nên bắn cảnh báo 100% đóng luôn cờ 80% để lần sau không gửi ngược cảnh báo nhẹ hơn.*
  - [!] Gửi push notification — *Vẫn dùng `LoggingPushNotificationService` (log-only). **Cùng gap đã ghi nhận ở Phase 4**: chưa có push provider (FCM) được duyệt trong `TECH_STACK.md`, không tự thêm dependency (AGENTS.md §5). Logic chọn-ai-để-gửi đã hoàn chỉnh (tôn trọng `NotificationPreferences.PushEnabled`/`BudgetAlertsEnabled`), chỉ thiếu kênh gửi thật.*
  - [x] Update sent timestamps
  - *(Lịch chạy `0 * * * *` — mỗi giờ, đúng `ARCHITECTURE.md` §5.)*

### Backend — Saving Goals Module

- [x] Entity: `SavingGoal`, `GoalContribution` — *Hoàn thiện bảng tối thiểu Phase 4 tạo ra: `status` từ `string` thành enum `SavingGoalStatus` có CHECK constraint (đúng pattern `TransactionConfiguration`), thêm `completed_at` và `deadline_notified_at`.*
- [x] Repository: `ISavingGoalRepository`
- [x] Command: `CreateSavingGoalCommand` + Handler + Validator
- [x] Command: `UpdateSavingGoalCommand` + Handler — *Chỉ sửa được goal `Active` (ngược lại 422 `SAVING_GOAL_NOT_ACTIVE`); hạ target xuống dưới `saved_cents` thì auto-complete.*
- [x] Command: `ContributeToGoalCommand` + Handler
  - [x] Cộng vào saved_cents
  - [x] Auto-complete nếu đạt target
  - [!] Trigger Mascot celebration — *Blocked by Phase 7: Gamification module chưa tồn tại. Push "chúc mừng hoàn thành" đã gửi (qua stub log-only như trên), chỉ thiếu phần mascot.*
- [x] Command: `CancelSavingGoalCommand` + Handler — *Giữ nguyên `saved_cents` và lịch sử đóng góp; hủy không phải xóa.*
- [x] Query: `GetSavingGoalListQuery`
- [x] Query: `GetGoalProgressQuery` (on-track calculation) — *So tiến độ thực tế với tiến độ tuyến tính kỳ vọng từ `created_at` tới `deadline`. Không có deadline → luôn on-track (không có nhịp bắt buộc để lệch). Mốc kỳ vọng trôi liên tục theo thời gian nên so bằng "≥" đúng nghĩa đen sẽ lật trạng thái vì vài mili-giây (user góp đúng 50% ở đúng nửa chặng vẫn bị coi là trễ) → cho biên 1% mục tiêu.*
- [x] Controller: `SavingGoalsController` — *`POST /{id}/cancel` dùng action-verb thay vì `DELETE` vì hủy không xóa dữ liệu (`CONVENTIONS.md` §1.1).*
- [x] Job: `GoalDeadlineCheckJob` — check goals quá deadline — *Lịch `0 8 * * *`; `ARCHITECTURE.md` §5 không quy định giờ cho job này nên chọn 08:00 (khung giờ nhắc hợp lý, không trùng job nặng chạy đêm). Nhắc đúng 1 lần qua `deadline_notified_at` và **không tự đổi status** — gia hạn hay hủy là quyết định của user.*

### Tests

- [x] Unit: Budget alert threshold logic — *`BudgetAlertJobTests`: 80% và 100% mỗi ngưỡng bắn 1 lần, chạy lại không bắn lại, nhảy thẳng qua 100% không kéo theo cảnh báo 80%, tôn trọng `BudgetAlertsEnabled`/`PushEnabled`.*
- [x] Unit: Goal auto-complete khi đạt target — *`ContributeToGoalCommandHandlerTests`, gồm cả trường hợp góp vượt target và góp vào goal đã completed/cancelled (422).*
- [x] Unit: On-track calculation — *`GetGoalProgressQueryHandlerTests`: đúng nhịp / trễ nhịp / không deadline / quá hạn / đã hoàn thành.*
- [x] Unit: `BudgetPeriodServiceTests` — *Biên chu kỳ UTC+7, get-or-create period, apply/revert đối xứng, clamp không âm, `Credit` không tiêu hạn mức, 1 giao dịch cập nhật đồng thời budget category + budget tổng.*
- [x] Unit: `CreateBudgetCommandHandlerTests`, `UpdateBudgetLimitCommandHandlerTests`
- [x] Integration: `BudgetsControllerTests` — *chi tiêu cập nhật cả 2 budget → đổi category chuyển spend sang budget khác → xóa giao dịch revert về 0; backfill khi tạo budget giữa tháng; `Credit` không tiêu hạn mức; trùng budget → 409; cross-user isolation.*
- [x] Integration: `SavingGoalsControllerTests` — *tạo → góp nhiều lần → progress → auto-complete → góp tiếp 422 → cancel; filter theo status; cross-user isolation.*

**Gỡ TODO `[!] Blocked by Phase 5` để lại từ Phase 4:** `IBudgetPeriodService` giờ được gọi trong `ConfirmTransactionCommandHandler`, `UpdateTransactionCommandHandler` (revert theo category/số tiền/ngày CŨ rồi áp giá trị MỚI — cả ba đều có thể trỏ sang budget khác và chu kỳ khác) và `DeleteTransactionCommandHandler`. Bổ sung thêm `CreateManualTransactionCommandHandler`: Phase 4 không đánh dấu `[!]` ở đây vì bỏ sót, nhưng giao dịch thủ công vào thẳng `Status=Confirmed` nên phải tiêu hạn mức ngay tại đó chứ không qua bước confirm. Phần `[!] Blocked by Phase 7` (EXP/streak/mission) giữ nguyên.

---

## Phase 6 — Domain 9: Reports & Analytics

### Database

- [ ] Migration: tạo bảng `daily_summaries`
- [ ] Migration: tạa bảng `spending_insights`

### Backend

- [ ] Query: `GetMonthlySummaryQuery` + Handler
- [ ] Query: `GetCategoryBreakdownQuery` + Handler
- [ ] Query: `GetTransactionTimelineQuery` + Handler (cursor pagination)
- [ ] Query: `GetSpendingForecastQuery` + Handler (gọi AI Service)
- [ ] Query: `GetSpendingInsightsQuery` + Handler
- [ ] Controller: `ReportsController`
- [ ] Job: `DailySummaryJob`
- [ ] Job: `InsightGeneratorJob`
  - [ ] vs_last_month insight
  - [ ] recurring_detected insight
  - [ ] unusual_spending insight

### Tests

- [ ] Unit: Forecast outlier detection
- [ ] Unit: Monthly summary calculation
- [ ] Unit: Category breakdown percentage

---

## Phase 7 — Domain 10: Gamification

### Database

- [ ] Migration: tạo bảng `user_gamification`
- [ ] Migration: tạo bảng `mascot_items`
- [ ] Migration: tạo bảng `user_mascot_items`
- [ ] Migration: tạo bảng `missions`
- [ ] Migration: tạo bảng `user_missions`
- [ ] Seed: default missions (daily/weekly/one_time)
- [ ] Seed: default mascot items (free tier)

### Backend

- [ ] Entity: `UserGamification`, `Mission`, `UserMission`, `MascotItem`, `UserMascotItem`
- [ ] Repository: `IGamificationRepository`, `IMissionRepository`
- [ ] Service: `IGamificationService`
  - [ ] `AwardExpAsync()` — cộng EXP, check level up
  - [ ] `UpdateStreakAsync()` — cập nhật streak
  - [ ] `CheckMissionConditionsAsync()` — evaluate missions
  - [ ] `UnlockMascotItemAsync()` — unlock item khi đủ điều kiện
- [ ] Query: `GetGamificationProfileQuery`
- [ ] Query: `GetActiveMissionsQuery`
- [ ] Query: `GetMissionHistoryQuery`
- [ ] Query: `GetMascotInventoryQuery`
- [ ] Command: `UpdateMascotOutfitCommand`
- [ ] Controller: `GamificationController`
- [ ] Job: `StreakCheckJob`
- [ ] Job: `MissionResetJob`

### Tests

- [ ] Unit: EXP level-up threshold
- [ ] Unit: Streak reset logic (timezone-aware)
- [ ] Unit: Mission condition evaluators

---

## Phase 8 — Domain 11: Admin

### Backend

- [ ] Middleware/Policy: `AdminOnly` authorization policy
- [ ] Controller: `AdminUsersController`
  - [ ] List users (no financial data)
  - [ ] Lock/unlock user
- [ ] Controller: `AdminProviderConfigsController`
  - [ ] CRUD provider configs
- [ ] Controller: `AdminCategoriesController`
  - [ ] Manage system categories
- [ ] Controller: `AdminMissionsController`
  - [ ] CRUD missions, toggle active
- [ ] Controller: `AdminAIStatsController`
  - [ ] Aggregate AI metrics (gọi AI Service)
- [ ] Controller: `AdminAuditLogsController`
  - [ ] Filter và view audit logs

---

## Phase 9 — AI Service

### Database (AI DB)

- [ ] Alembic migration: `raw_samples`
- [ ] Alembic migration: `labeled_samples`
- [ ] Alembic migration: `sample_splits`
- [ ] Alembic migration: `provider_patterns`
- [ ] Alembic migration: `model_versions`
- [ ] Alembic migration: `training_jobs`
- [ ] Alembic migration: `evaluation_runs` + `evaluation_category_metrics` + `evaluation_predictions`
- [ ] Alembic migration: `pipeline_requests`
- [ ] Alembic migration: `user_feedback` + `feedback_batch_jobs`
- [ ] Alembic migration: `ab_experiments` + `ab_assignments` + `ab_metrics`
- [ ] Seed: provider_patterns cho MB Bank, Vietcombank, MoMo, ZaloPay

### AI Pipeline

- [ ] `orchestrator.py` — điều phối stages
- [ ] `stages/classifier.py` — Financial/Non-financial classifier
- [ ] `stages/extractor.py` — Amount, merchant, date extraction
  - [ ] Rule-based extraction fallback per provider
  - [ ] VND amount parser ("75k", "1.5tr", "75,000")
- [ ] `stages/categorizer.py` — Category + confidence
- [ ] `stages/duplicate_detector.py` — Dedup trong 5 phút
- [ ] `api/v1/pipeline.py` — POST /api/v1/analyze endpoint
- [ ] `api/v1/feedback.py` — POST /api/v1/feedback endpoint
- [ ] `utils/anonymizer.py` — SHA-256 hash, body anonymization

### AI Training & Evaluation

- [ ] `scripts/train.py` — training script
- [ ] `scripts/evaluate.py` — evaluation script
- [ ] `scripts/feedback_batch.py` — convert feedback → labeled_samples

### Tests

- [ ] Unit test classifier với fixture notifications từ 5 providers
- [ ] Unit test extractor: VND parser, date parser
- [ ] Unit test categorizer
- [ ] Integration test full pipeline flow

---

## Backlog (Future — Không trong MVP scope)

- [ ] Receipt OCR (chụp hóa đơn)
- [ ] Voice input
- [ ] iOS support
- [ ] Recurring transaction detection
- [ ] Subscription tracking
- [ ] Multi-currency support
- [ ] Export to CSV/Excel
- [ ] Import bank statement (CSV)
- [ ] A/B testing infrastructure cho AI models
- [ ] Real-time spending forecast với streaming
- [ ] Marketplace phần thưởng
- [ ] Household/shared account

---

## Progress Summary

| Phase | Status | Tasks Done / Total |
|---|---|---|
| Phase 0 — Setup | `[x]` | 15 / 15 |
| Phase 1 — Auth & Profile | `[x]` | 25 / 25 *(+1: Google login, kéo từ Backlog)* |
| Phase 2 — Financial Accounts | `[x]` | 11 / 11 *(guard has-transactions hoàn thành ở Phase 4, xem note dưới)* |
| Phase 3 — Categories | `[x]` | 8 / 8 |
| Phase 4 — Notifications & Transactions | `[x]` | 28 / 28 *(3 sub-task cascade budget đã gỡ block ở Phase 5; còn 4 sub-task EXP/streak/mission `[!]` Blocked by Phase 7)* |
| Phase 5 — Budget & Goals | `[x]` | 22 / 22 *(1 sub-task "gửi push notification" của `BudgetAlertJob` và 1 sub-task "Mascot celebration" đánh dấu `[!]` — xem note; toàn bộ phần buildable đã xong và đã gỡ hết TODO Blocked by Phase 5 của Phase 4)* |
| Phase 6 — Reports | `[ ]` | 0 / 12 |
| Phase 7 — Gamification | `[ ]` | 0 / 20 |
| Phase 8 — Admin | `[ ]` | 0 / 12 |
| Phase 9 — AI Service | `[ ]` | 0 / 24 |
| **Total** | | **109 / 176** |

---

**Verify Phase 2 (2026-09-10):** `dotnet build` sạch 0 warning; `dotnet test` xanh 20/20 (chạy qua container SDK 9.0); `dotnet ef migrations has-pending-model-changes` sạch; `docker compose up` full stack từ volume rỗng — migration áp dụng sạch, seed đủ 5 provider configs; smoke test curl end-to-end (register/login → tạo bank account → tạo trùng package_name [409] → tạo cash account → list → toggle monitoring → get balance → delete → get balance sau xóa [404]) đều đúng như thiết kế. Một bug hạ tầng test được tìm thấy và sửa: `AuthApiFactory` set config qua `Environment.SetEnvironmentVariable` (process-wide) — khi 2 test class dùng factory riêng chạy song song (xunit mặc định chạy khác class song song), race trên biến môi trường khiến 2 `WebApplicationFactory` đôi khi trỏ cùng lúc vào 1 Postgres container, gây deadlock/connection-refused ngẫu nhiên. Fix bằng cách gom mọi integration test class vào chung 1 `[Collection("Integration")]` để chạy tuần tự.

**Verify Phase 3+4 (2026-09-10):** `dotnet build` sạch 0 warning; `dotnet test` xanh 94/94, chạy lặp lại 2 lần liên tiếp không flake (chạy qua container SDK 9.0); `dotnet ef migrations has-pending-model-changes` sạch; `docker compose up` full stack (bao gồm `ai-service` thật — vẫn chỉ là FastAPI scaffold trống của Phase 0, chưa có route `/api/v1/analyze`) — smoke test curl end-to-end: seed đủ 11 category; tạo cash account → tạo manual transaction (debit) → balance giảm đúng → xóa account khi còn giao dịch bị 409 → xóa transaction → balance khôi phục → xóa account thành công; gọi `/notifications/analyze` với package đã whitelist nhắm vào `ai-service` thật (chưa có route) → xác nhận trả đúng `503 NOTIFICATION_AI_SERVICE_UNAVAILABLE` (không crash 500), `notification_logs.status='failed'` đúng trong DB, log không có exception chưa xử lý. Nhánh AI thành công (financial → tạo transaction draft → confirm) được cover đầy đủ qua integration test với `FakeAIServiceClient` (chưa thể verify qua curl thật vì Phase 9 chưa code AI Service) — cùng giới hạn đã ghi nhận ở Google login.

**Verify Phase 5 (2026-09-11):** `dotnet build` sạch 0 warning; `dotnet test` xanh 153/153 (chạy qua container SDK 9.0); `dotnet ef migrations has-pending-model-changes` sạch; `docker compose up --build` full stack từ volume rỗng — migration áp dụng sạch, 4 recurring job đăng ký đủ (`budget-alerts`, `goal-deadline-check`, `data-deletion`, `retry-failed-notifications`), log không có exception chưa xử lý (ngoài 1 lỗi `__EFMigrationsHistory` lúc khởi động DB rỗng, EF tự xử lý, đã có từ các phase trước). Smoke test curl end-to-end: seed đủ 11 category → tạo budget food + budget tổng → tạo budget food lần 2 nhận 409 → chi 250k vào food thì cả 2 budget cùng lên 250k → đổi giao dịch sang transport thì food về 0 còn budget tổng giữ 250k → ghi nhận thu nhập 15tr không tiêu hạn mức nào → xóa giao dịch thì mọi budget về 0 → nâng hạn mức food lên 2tr, `%` tính lại đúng → tạo mục tiêu 10tr, góp 3tr (30%, on-track, cần 77.778đ/ngày) → góp nốt 7tr thì status thành `completed` → góp tiếp nhận 422 → **số dư tài khoản không đổi vì đóng góp mục tiêu là bookkeeping thuần** (10tr ban đầu + 15tr thu nhập = 25tr).

Hai lỗi tìm được khi chạy thật (không lộ ra ở unit test) và đã sửa: (1) Npgsql từ chối `DateTimeOffset` offset +07:00 cho cột `timestamptz` khiến mọi endpoint budget trả 500 — mốc chu kỳ giờ trả về ở UTC, xem note `BudgetCalendar` ở trên; (2) response summary có field `totalSpentCents` = 0 nằm ngay trên dòng budget tổng đang hiện 250k (vì nó chỉ cộng budget theo category) — đã tách hẳn `TotalBudget` khỏi `CategoryBudgets` để không còn con số nào đọc nhầm được.

*Last updated: 2026-09-11*
*Next priority: Phase 6 — Domain 9: Reports & Analytics (hoặc Phase 7 — Gamification nếu muốn gỡ nốt TODO `[!] Blocked by Phase 7` còn lại trong Transaction handlers và Saving Goals)*
