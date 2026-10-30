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
  - [x] EXP award — *Hoàn thành ở Phase 7 (2026-09-11): `+10` (`TransactionExpRewards.ConfirmTransaction`).*
  - [x] Streak check — *Hoàn thành ở Phase 7 (2026-09-11): qua `GamificationService.RecordActivityAsync`, tính theo lịch VN.*
  - [x] Mission condition trigger — *Hoàn thành ở Phase 7 (2026-09-11): `MissionConditionType.ConfirmTransaction`.*
- [x] Command: `CreateManualTransactionCommand` + Handler + Validator — *Tạo trực tiếp `Status=Confirmed` (không qua bước confirm riêng vì không có AI draft), cascade balance ngay.*
- [x] Command: `UpdateTransactionCommand` + Handler + Validator
  - [x] Revert budget nếu category/amount/date thay đổi — *Hoàn thành ở Phase 5 (2026-09-11): revert theo category/số tiền/ngày CŨ trước khi ghi đè entity, rồi áp giá trị MỚI — cả ba đều có thể trỏ sang budget khác và chu kỳ khác.*
  - [x] Lưu AI correction nếu category thay đổi — *`POST /api/v1/feedback` best-effort, chỉ khi `Transaction.Source=Notification` (có category AI dự đoán để so sánh).*
- [x] Command: `DeleteTransactionCommand` + Handler
  - [x] Revert budget nếu đã confirmed — *Hoàn thành ở Phase 5 (2026-09-11).*
  - [x] Revert EXP — *Hoàn thành ở Phase 7 (2026-09-11): `RevertExpAsync` trừ lại đúng `+10` đã cộng lúc confirm. Có thể làm tụt level — đánh đổi có chủ ý để không farm được bằng cách thêm rồi xóa.*
  - *(Revert `FinancialAccount.BalanceCents` khi xóa giao dịch đã Confirmed — không bị block, đã làm.)*
- [x] Command: `ParseNaturalLanguageCommand` + Handler (gọi AI Service) — *Tái dùng `POST /api/v1/analyze` với `package_name="manual_entry"` thay vì thêm route AI Service mới (đã hỏi user trước khi quyết định, theo AGENTS.md §5 — đổi API contract Backend↔AI Service cần approval). Không persist, chỉ trả field để client prefill form tạo manual transaction.*
- [x] Query: `GetTransactionListQuery` + Handler (filter, cursor pagination)
- [x] Query: `GetTransactionDetailQuery` + Handler
- [x] Controller: `TransactionsController`

### Tests — Critical

- [x] Unit: `ConfirmTransactionCommandHandler`
  - [x] Cascade balance update (debit/credit đúng chiều) — *viết ở Phase 4 thay cho "cascade budget update" vì lúc đó Budget module chưa có; phần budget được bổ sung ở Phase 5, xem note gỡ block cuối Phase 5.*
  - [x] EXP award — *Hoàn thành ở Phase 7 (2026-09-11): `HandleAsync_Confirming_AwardsExpAndAdvancesConfirmMissions` + `HandleAsync_AlreadyConfirmed_AwardsNothing`.*
  - [x] Streak update — *Hoàn thành ở Phase 7 (2026-09-11): phủ trong `GamificationServiceTests` (ngày liên tiếp, cùng ngày không đếm 2 lần, đứt chuỗi, lịch VN) thay vì lặp lại ở test của handler.*
  - [x] Rollback khi lỗi — *test "đã confirmed rồi không confirm lại được" (422 `TRANSACTION_NOT_DRAFT`).*
- [x] Unit: `DeleteTransactionCommandHandler` — revert logic (balance + budget + EXP; phần EXP hoàn thành ở Phase 7)
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
- [x] Biên chu kỳ (`BudgetCalendar`, đã chuyển thành `Common/VietnamTime` ở Phase 6) — *Tính theo **UTC+7 cố định** thay vì `TimeZoneInfo.FindSystemTimeZoneById`: `InvariantGlobalization=true` bật solution-wide làm API phụ thuộc ICU không đáng tin (cùng lý do đã buộc viết lại `Slugify` ở Phase 3), và app chỉ phục vụ VN. Giao dịch lúc 18:00 UTC ngày cuối tháng thuộc về tháng sau theo giờ VN — có test chốt. **Mốc chu kỳ luôn trả về ở offset 0**: Npgsql từ chối ghi `DateTimeOffset` có offset khác 0 vào cột `timestamptz`, kể cả khi chỉ dùng làm tham số truy vấn (lỗi này làm mọi endpoint budget trả 500 lúc đầu). Hệ quả: (a) muốn năm/tháng để dựng cache key thì phải dùng `VietnamTime.YearMonthOf` (tên cũ: `BudgetCalendar.VietnamYearMonth`), đọc thẳng `.Year`/`.Month` của giá trị UTC sẽ ra tháng trước; (b) `start.AddMonths(1)` không phải mốc cuối chu kỳ vì cộng tháng lên một mốc UTC lệch khi 2 tháng khác số ngày.*
- [x] Command: `CreateBudgetCommand` + Handler + Validator — *Tạo budget giữa tháng thì backfill `spent_cents` từ giao dịch đã Confirmed trong chu kỳ (`ITransactionRepository.SumConfirmedSpendAsync`), nếu không budget mới luôn hiện 0 dù user đã tiêu cả tháng.*
- [x] Command: `UpdateBudgetLimitCommand` + Handler — *Chỉ period đang chạy nhận limit mới; period quá khứ giữ snapshot. Nâng hạn mức qua ngưỡng thì reset cờ alert tương ứng để cảnh báo bắn lại được.*
- [x] Command: `DeleteBudgetCommand` + Handler — *Soft delete, giữ `budget_periods` làm lịch sử.*
- [x] Query: `GetBudgetSummaryQuery` (current month overview) — *Cache Redis 5 phút theo `ARCHITECTURE.md` §6 (`CacheKeys.BudgetSummary`), invalidate ở cả 3 command budget lẫn 4 handler transaction. Response tách `TotalBudget` (budget tổng, nullable) khỏi `CategoryBudgets` thay vì gộp một con số "tổng": budget tổng bao trùm mọi category nên mọi cách cộng chung đều hoặc tính trùng, hoặc cho ra field tên "total spent" nhưng không phải tổng chi tiêu — smoke test thật đã lộ đúng cái bẫy đó. `PeriodStart`/`PeriodEnd` trả về ở `+07:00` để client đọc "01/09 → 01/10", còn tầng lưu trữ vẫn dùng UTC.*
- [x] Controller: `BudgetsController`
- [x] Job: `BudgetAlertJob`
  - [x] Query budget_periods gần limit — *Join tường minh sang `_context.Budgets` thay vì `Include`: join áp query filter soft-delete (budget đã xóa ngừng cảnh báo) và mang theo `UserId` mà `budget_periods` không lưu.*
  - [x] Check `alert_80_sent_at` và `alert_100_sent_at` — *Mỗi ngưỡng bắn đúng 1 lần/chu kỳ. Chi tiêu có thể nhảy thẳng từ dưới 80% lên quá 100% giữa 2 lần chạy, nên bắn cảnh báo 100% đóng luôn cờ 80% để lần sau không gửi ngược cảnh báo nhẹ hơn.*
  - [x] Gửi push notification — *Kênh thật qua FCM từ Phase 12 (user duyệt dependency ngày 2026-09-14). Không có credentials thì tự lùi về `LoggingPushNotificationService` — xem Phase 12.*
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
  - [x] Trigger Mascot celebration — *Hoàn thành ở Phase 7 (2026-09-11): `ContributeToGoalCommandHandler` trả `celebration` trong response để client diễn hoạt ngay; đã verify curl (mở khóa 3 mascot item khi hoàn thành mục tiêu). Push "chúc mừng hoàn thành" vẫn đi qua stub log-only — xem gap FCM ở dòng `BudgetAlertJob`.*
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

**Quyết định đã hỏi user (AGENTS.md §5):** `GetSpendingForecastQuery` **tính bằng thống kê ngay trong backend**, KHÔNG thêm route `/forecast` vào contract Backend↔AI Service (khác chữ "gọi AI Service" trong task gốc). Lý do: ngoại suy run-rate vốn là phép thống kê, đẩy qua HTTP sang AI Service không làm nó chính xác hơn mà lại làm tính năng báo cáo chết cho tới khi Phase 9 xong. `ISpendingForecaster` là chỗ Phase 9 cắm model AI vào mà không phải đổi controller hay DTO.

### Database

- [x] Migration: tạo bảng `daily_summaries` — *`summary_date` dùng `DateOnly` map sang `date`, tránh hẳn bẫy offset của `timestamptz`. Unique `(user_id, summary_date)` để job chạy lại cùng ngày thì upsert.*
- [x] Migration: tạo bảng `spending_insights` — *Chống sinh trùng bằng **2 partial unique index**: `vs_last_month` không có category, mà Postgres coi mỗi NULL là distinct nên 1 index gộp sẽ để job đẻ insight trùng mỗi đêm — đúng cái bẫy budget tổng đã dính ở Phase 5. Gộp 1 migration `CreateReportTables`.*

### Backend

- [x] Query: `GetMonthlySummaryQuery` + Handler — *Đọc `daily_summaries` cho ngày đã chốt và **lấp mọi ngày chúng chưa phủ bằng dữ liệu live**. Không suy "thiếu dòng summary" thành "ngày đó không tiêu gì" được: nó cũng đúng khi `DailySummaryJob` chưa chạy — smoke test thật đã bắt lỗi này (tổng tháng ra 300k trong khi breakdown/timeline ra 900k). Xem note Verify bên dưới.*
- [x] Query: `GetCategoryBreakdownQuery` + Handler — *Tính live vì `daily_summaries` chỉ giữ top category. Phần trăm làm tròn xuống rồi dồn phần thiếu cho mục lớn nhất để tổng luôn đúng 100.*
- [x] Query: `GetTransactionTimelineQuery` + Handler (cursor pagination) — ***Tái dùng `ITransactionRepository.GetListAsync`*** đã có cursor pagination từ Phase 4, handler chỉ gom kết quả theo ngày giờ VN. Không viết pagination lần hai.*
- [x] Query: `GetSpendingForecastQuery` + Handler — *`StatisticalSpendingForecaster`: giữ nguyên phần đã tiêu thật, chỉ ngoại suy số ngày còn lại bằng trung vị + loại outlier theo MAD. Ngày không có giao dịch tính là 0 chứ không bỏ qua, nếu không run-rate thành "chi tiêu mỗi ngày CÓ tiêu". Trả kèm `Confidence` low/medium/high để client không trình bày con số dựng từ vài ngày như thể chắc chắn. Nhận đồng hồ qua constructor để test không phụ thuộc ngày trong tháng.*
- [x] Query: `GetSpendingInsightsQuery` + Handler
- [x] Controller: `ReportsController` — *thêm `PATCH /insights/{id}/read`.*
- [x] Job: `DailySummaryJob` — *`5 17 * * *` UTC = **00:05 giờ VN**. Hangfire đọc cron theo UTC còn ARCHITECTURE.md §5 ghi giờ vận hành (giờ VN) nên phải quy đổi tường minh. Chi tiêu chưa phân loại không bao giờ được chọn làm top category.*
- [x] Job: `InsightGeneratorJob` — *`0 19 * * *` UTC = **02:00 giờ VN**. Mọi insight kiểm tra tồn tại trước khi ghi nên chạy mỗi đêm không tích lũy bản trùng.*
  - [x] vs_last_month insight — *So **cùng kỳ** (cùng số ngày đã trôi), không so cả tháng trước với nửa tháng này — làm thế thì lúc nào cũng ra "bạn tiêu ít hơn". Ngưỡng ≥15%; tháng trước không tiêu gì thì im lặng chứ không báo tăng vô hạn.*
  - [x] recurring_detected insight — *Cần đủ cả 3: ≥3 lần, số tiền lệch ≤10%, khoảng cách 25–35 ngày. Thiếu một điều kiện thì quán quen giá thay đổi sẽ bị gắn nhãn thuê bao.*
  - [x] unusual_spending insight — *So với chính category đó bằng median + 3·MAD, cần ≥5 mẫu để không gọi giao dịch thứ hai trong một danh mục là bất thường.*

### Tests

- [x] Unit: Forecast outlier detection — *`StatisticsTests` (median/MAD/trim, thuần hàm) + `SpendingForecasterTests` với đồng hồ cố định.*
- [x] Unit: Monthly summary calculation — *gồm cả trường hợp job chưa chạy và tháng chốt dở dang không được cộng trùng.*
- [x] Unit: Category breakdown percentage — *tổng luôn 100 kể cả khi chia lẻ; nhóm chưa phân loại tách riêng.*
- [x] Unit: `DailySummaryJobTests`, `InsightGeneratorJobTests`, `VietnamTimeTests`
- [x] Integration: `ReportsControllerTests`

---

## Phase 7 — Domain 10: Gamification

**Hai quyết định đã hỏi user (AGENTS.md §5):**
1. **EXP bậc 2, chậm dần**: tổng EXP để đạt level N = `50·N·(N-1)` (lvl2=100, lvl3=300, lvl4=600, lvl5=1000). Nguồn EXP: xác nhận giao dịch +10, giao dịch thủ công +5, đóng góp mục tiêu +20, hoàn thành mission = `exp_reward` của mission.
2. **Mascot mở khóa theo level + mission**, không có tiền tệ ảo. Cột `is_premium` để dành chỗ cho tier trả phí sau, Phase 7 chưa seed item nào dùng tới.

### Database

- [x] Migration: tạo bảng `user_gamification` — *Tạo lazily lần đầu user phát sinh hoạt động, không backfill user cũ. Query đọc hồ sơ KHÔNG ghi dòng mới — mở màn hình không được tạo dữ liệu.*
- [x] Migration: tạo bảng `mascot_items` — *`unlock_level` và `unlock_mission_code` là 2 cột riêng thay vì một `unlock_value` đa nghĩa; CHECK constraint buộc mỗi `unlock_type` phải đi kèm đúng dữ liệu của nó, nếu không item có thể nằm ở trạng thái vĩnh viễn không mở được mà không ai nhận ra.*
- [x] Migration: tạo bảng `user_mascot_items` — *`item_type` nhân bản từ catalog để ràng buộc "mỗi loại chỉ mặc 1 món" là partial unique index thật; partial index của Postgres không tham chiếu được bảng join.*
- [x] Migration: tạo bảng `missions` — *`condition_type` là enum có tên + `condition_target` thay vì DSL JSON: tập điều kiện hữu hạn và biết trước nên kiểu có tên vừa dịch được sang SQL, vừa được compiler kiểm tra, vừa test vét cạn được.*
- [x] Migration: tạo bảng `user_missions` — *Dòng chu kỳ hiện tại sinh **lazily** khi user hoạt động hoặc mở màn hình mission (cùng pattern `budget_periods` ở Phase 5). Mission `one_time` dùng `period_start` quy ước cố định để unique index cho mỗi user đúng 1 dòng trọn đời. 5 bảng gộp 1 migration `CreateGamificationTables`.*
- [x] Seed: default missions (daily/weekly/one_time) — *7 mission; idempotent theo `code`, không ghi đè mission đã có vì Phase 8 sẽ cho admin sửa nội dung.*
- [x] Seed: default mascot items (free tier) — *8 item, tất cả `is_premium=false`.*

### Backend

- [x] Entity: `UserGamification`, `Mission`, `UserMission`, `MascotItem`, `UserMascotItem`
- [x] Repository: `IGamificationRepository`, `IMissionRepository`
- [x] Service: `IGamificationService` — *`RecordActivityAsync` là cửa vào duy nhất cho 4 handler, gộp cả 4 bước để không handler nào gọi thiếu. Giống `IBudgetPeriodService`: chỉ track, KHÔNG `SaveChangesAsync`, nên gamification nằm cùng 1 DB transaction ngầm với transaction/balance/budget.*
  - [x] `AwardExpAsync()` — cộng EXP, check level up — *EXP thưởng mission cộng trước khi tính lại level nên một hoạt động hoàn thành mission có thể nhảy nhiều bậc một lúc. `LevelForExp` dùng vòng lặp chứ không giải phương trình bậc 2 bằng `Math.Sqrt` — sai số dấu phẩy động có thể rơi ngay dưới mốc, mà mốc là đúng chỗ user để ý nhất.*
  - [x] `UpdateStreakAsync()` — *Theo lịch VN qua `VietnamTime.DateOf`. Cùng ngày không cộng dồn; đứt quãng về 1; `longest_streak_days` giữ nguyên vì đó là kỷ lục, không phải trạng thái.*
  - [x] `CheckMissionConditionsAsync()` — *Mission `LoginStreak` lấy thẳng giá trị streak chứ không tăng dần, vì streak là trạng thái chứ không phải số lần ghi nhận.*
  - [x] `UnlockMascotItemAsync()` — *Item mở theo mission phải xét cả mission vừa hoàn thành **trong chính lượt này**: nó mới được track chứ chưa lưu nên truy vấn DB không thấy, không xử lý riêng thì phần thưởng chỉ xuất hiện ở lần hoạt động kế tiếp.*
- [x] Query: `GetGamificationProfileQuery` — *cache `user:{id}:gamification` 10 phút (ARCHITECTURE.md §6).*
- [x] Query: `GetActiveMissionsQuery` — *cache `user:{id}:active_missions` 30 phút. Mission chưa được chạm tới vẫn hiện với tiến độ 0 dù chưa có dòng DB — user không thể làm nhiệm vụ mình không nhìn thấy.*
- [x] Query: `GetMissionHistoryQuery`
- [x] Query: `GetMascotInventoryQuery` — *trả cả item chưa sở hữu kèm điều kiện mở khóa.*
- [x] Command: `UpdateMascotOutfitCommand` — *validate quyền sở hữu (không sở hữu và không tồn tại đều 404) + mỗi loại tối đa 1 món (422 `GAMIFICATION_DUPLICATE_MASCOT_SLOT`).*
- [x] Controller: `GamificationController`
- [x] Job: `StreakCheckJob` — *`55 16 * * *` UTC = **23:55 giờ VN**. Chạy cuối ngày chứ không đầu ngày hôm sau để user mở app lúc 23:58 vẫn kịp giữ chuỗi.*
- [x] Job: `MissionResetJob` — *`1 17 * * *` UTC = **00:01 giờ VN**. **Không** tạo sẵn dòng cho chu kỳ mới — tạo sẵn mọi user × mọi mission mỗi ngày sẽ đẻ hàng loạt dòng tiến độ 0 của người không dùng app.*

### Tests

- [x] Unit: EXP level-up threshold — *`LevelCurveTests`: biên 99/100/101, nhảy nhiều bậc, chặn trên chống lặp vô hạn.*
- [x] Unit: Streak reset logic (timezone-aware) — *cùng ngày / liền ngày / đứt quãng, và mốc chuyển ngày 17:00 UTC theo giờ VN.*
- [x] Unit: Mission condition evaluators — *tiến độ, hoàn thành, không trả thưởng hai lần, điều kiện streak, `MissionCalendarTests` (Chủ nhật thuộc tuần bắt đầu từ thứ Hai).*
- [x] Unit: `GamificationServiceTests` (mascot unlock theo level/mission, revert EXP), `StreakCheckJobTests`, `MissionResetJobTests`
- [x] Integration: `GamificationControllerTests`

**Gỡ 4 TODO `[!] Blocked by Phase 7` của Phase 4/5:** `ConfirmTransactionCommandHandler` (+10), `CreateManualTransactionCommandHandler` (+5), `ContributeToGoalCommandHandler` (+20, kèm mascot celebration trả trong response để client diễn hoạt ngay), `DeleteTransactionCommandHandler` (revert EXP). **Backend hiện không còn TODO nào bị block bởi phase khác.**

**Lưu ý về revert EXP:** xóa giao dịch đã confirmed trừ lại đúng số EXP đã cộng, nên **có thể làm tụt level**. Đây là đánh đổi có chủ ý — không hoàn EXP thì user farm được bằng cách thêm rồi xóa giao dịch liên tục. Tiến độ mission KHÔNG hoàn lại (mission đã hoàn thành là việc đã xảy ra trong chu kỳ đó). Nếu sau này thấy trải nghiệm tụt level tệ hơn nguy cơ farm thì chỉ cần bỏ lời gọi `RevertExpAsync` trong `DeleteTransactionCommandHandler`.

---

## Phase 8 — Domain 11: Admin

> **Ba quyết định đã chốt với user trước khi làm (2026-09-12):**
> 1. **AI stats lấy từ CẢ HAI nguồn.** Backend tự tổng hợp "AI chạy tốt đến đâu trong sản xuất"
>    từ `ai_results` × `transactions` — trong đó **tỉ lệ người dùng sửa lại danh mục AI đoán**
>    là thước đo thật nhất và chỉ tồn tại ở backend DB (AI DB thấy dự đoán của chính nó nhưng
>    không bao giờ thấy người dùng làm gì với nó). Thêm `GET /api/v1/stats` vào contract cho
>    phần "model và dataset đang ở đâu" — **đổi contract, user đã duyệt.**
> 2. **Chỉ tắt, không xoá.** Giao dịch cũ trỏ tới category, ví người dùng trỏ tới provider
>    config, `user_missions` trỏ tới mission — xoá thật làm hỏng lịch sử của người khác.
> 3. **Thêm cột `categories.is_active`** (migration `AddIsActiveToCategories`). `provider_configs`
>    và `missions` đã có sẵn cờ này, riêng `categories` thì không; dùng `deleted_at` thay thế sẽ
>    hỏng ngầm vì `Category` có global query filter `deleted_at IS NULL` mà
>    `ReportRepository.GetCategorySpendAsync` đi qua navigation `Transaction.Category` — tắt một
>    danh mục là chi tiêu cũ của nó rơi khỏi category-breakdown mà không báo gì.

### Backend

- [x] Middleware/Policy: `AdminOnly` authorization policy — *`RequireRole(nameof(UserRole.Admin))`, khớp `ClaimTypes.Role` mà `TokenService` gắn lúc đăng nhập. Gom vào `AdminControllerBase` để attribute được KẾ THỪA chứ không phải gõ lại: quên một lần là endpoint rơi về `FallbackPolicy`, tức bất kỳ ai đã đăng nhập cũng gọi được, mà response vẫn 200 nên không có gì báo. **Vai trò nằm TRONG access token, không tra lại DB mỗi request** — khoá hay hạ quyền một admin không đuổi được phiên đang chạy, họ còn vào được tối đa 15 phút (`JWT_ACCESS_TTL_MINUTES`); đó là biên trên đã biết của thiết kế JWT hiện tại.*
- [x] Controller: `AdminUsersController`
  - [x] List users (no financial data) — *`AdminUserDto` đơn giản là KHÔNG có chỗ để đặt số dư hay số tiền (ARCHITECTURE.md §7.3); ánh xạ tường minh trong `AdminMapper` chứ không qua AutoMapper để entity mọc thêm cột không lặng lẽ lọt ra. Phân trang keyset trên `(created_at, id)`, tái dùng đúng kiểu con trỏ + `ApiMeta(Cursor)` đã có. Có test đọc THÔ response và assert không chứa `amountCents`/`balanceCents`/`passwordHash`.*
  - [x] Lock/unlock user — *`PATCH /{id}/lock` nhận TRẠNG THÁI mong muốn (đúng ví dụ CONVENTIONS.md §1.1), idempotent, gọi lại không ghi thêm dòng audit. Khoá thì **thu hồi toàn bộ refresh token** — thiếu bước này thì "khoá" chỉ có tác dụng ở lần đăng nhập sau, tức là không bao giờ. Admin không tự khoá được mình (422 `ADMIN_CANNOT_LOCK_SELF`) nhưng **tự mở khoá được** — chặn cả chiều mở là chặn nhầm lối thoát.*
- [x] Controller: `AdminProviderConfigsController`
  - [x] CRUD provider configs — *Không có DELETE. `provider_key` **bất biến** sau khi tạo (422 `ADMIN_IMMUTABLE_FIELD`): `ProviderConfigSeeder` upsert theo nó và AI Service seed `provider_patterns` theo nó. Gửi lại đúng giá trị cũ thì chấp nhận — client PATCH thường gửi nguyên đối tượng vừa đọc về. `package_name` sửa được (ngân hàng có đổi package thật) nhưng có guard trùng và ghi chú tại chỗ về ràng buộc chéo sang AI DB.*
- [x] Controller: `AdminCategoriesController`
  - [x] Manage system categories — *Slug **bất biến**: đó là giá trị AI Service trả về trong `category_slug`, đổi đi là mọi giao dịch mới mất danh mục qua `GetSystemBySlugAsync` — im lặng. Slug nhận tường minh từ admin chứ không tự sinh từ tên như category người dùng: taxonomy phải khớp phía AI, không phải là hệ quả của cách gõ tiếng Việt. Category của một người dùng cụ thể trả **404** chứ không 403 để không tiết lộ nó tồn tại. `GetSystemBySlugAsync` nay lọc `is_active` — admin đã tắt một danh mục thì giao dịch MỚI không rơi vào đó nữa, draft không có danh mục và người dùng tự chọn.*
- [x] Controller: `AdminMissionsController`
  - [x] CRUD missions, toggle active — *`code`, `period_type` và `condition_type` **bất biến**; chỉ `title`/`description`/`condition_target`/`exp_reward` sửa được. Lý do: `user_missions` của chu kỳ ĐANG CHẠY đã tích tiến độ theo điều kiện cũ — đổi giữa chừng thì hoặc người dùng mất công đã bỏ ra, hoặc họ hoàn thành ngay mà chưa làm gì. Đổi target/thưởng có hiệu lực từ lần `MissionResetJob` kế tiếp. **Cache `user:{id}:active_missions` không invalidate hàng loạt được** (key theo từng user, không liệt kê ra được) nên nhiệm vụ vừa tắt còn hiện ở client tới hết TTL 30 phút — TTL chính là biên trên.*
- [x] Controller: `AdminAIStatsController`
  - [x] Aggregate AI metrics (gọi AI Service) — *Gộp hai nguồn theo quyết định #1. AI Service chết thì **vẫn trả 200** với `aiService: null` kèm lý do — một dashboard không được sập vì AI Service đang restart, nhất là khi nửa quan trọng hơn nằm ngay trong backend DB. Tỉ lệ trả về `null` khi mẫu số bằng 0 chứ không phải 0: "chưa có dữ liệu" khác hẳn "AI đoán đúng 100%", ép về 0 làm một hệ thống trống rỗng trông như một hệ thống hoàn hảo.*
- [x] Controller: `AdminAuditLogsController`
  - [x] Filter và view audit logs — *Chỉ ĐỌC, không có endpoint sửa/xoá — nhật ký kiểm toán mà admin sửa được thì không còn là nhật ký kiểm toán. Lọc theo `userId`/`eventType`/khoảng thời gian, phân trang keyset trên `(created_at, id)`. `metadata` trả nguyên chuỗi JSON, không parse: mỗi loại sự kiện một hình dạng, ép một schema vào sẽ làm hỏng đúng những dòng bất thường mà người xem đang đi tìm.*
- *(Thêm ngoài danh sách gốc)* `AuditEvents` — *8 chuỗi `"Auth.Login.Failed"`… rải rác từ Phase 1 chuyển thành hằng. Cần thiết vì `/admin/audit-logs` cho lọc theo chính giá trị đó: chuỗi rời thì không ai biết bộ giá trị hợp lệ gồm những gì, và một lỗi chính tả tạo ra loại sự kiện mới mà không ai nhận ra (AGENTS.md §3.4). **Giá trị chuỗi giữ nguyên** để dữ liệu lịch sử không mồ côi — kể cả `"Auth.TokenReuse.Detected"` vốn không theo đúng quy ước đặt tên.*
- *(Thêm ngoài danh sách gốc)* Mọi thao tác GHI của admin đều ghi một dòng `audit_logs` với `user_id` = ADMIN thực hiện, đối tượng bị tác động nằm trong `metadata`. Thao tác đọc thì không — nhật ký đầy dòng "admin đã mở trang danh sách" thì mất tác dụng của chính nó.

## Phase 9 — AI Service

> **Quyết định đã chốt với user trước khi làm (2026-09-11):**
> 1. **Rule bóc số tiền, ML phân loại.** Thông báo ngân hàng là template cố định nên Extractor
>    dùng regex — số tiền/ngày/số dư không bao giờ do model đoán, và khi sai thì
>    `matched_pattern_name` chỉ thẳng vào dòng cần sửa. ML chỉ đứng ở hai chỗ đầu vào mở:
>    Classifier và Categorizer (scikit-learn TF-IDF + LogisticRegression, nạp qua
>    `model_registry` theo version nên thay bằng PhoBERT sau này không phải sửa pipeline).
> 2. **Corpus:** user sẽ gửi mẫu thật từ 5 provider. Phase 9 dựng đường ống trước với corpus
>    bootstrap tổng hợp; cắm mẫu thật vào sau không cần sửa code.
> 3. **`requirements-training.txt` tách khỏi `requirements.txt`** — torch/transformers/
>    underthesea không vào image phục vụ. Không gỡ package nào khỏi `TECH_STACK.md` §2.2,
>    chỉ chia chỗ cài; image ~3GB → ~500MB.
> 4. **Hoãn 3 bảng `ab_*`** — A/B testing nằm ở Backlog "ngoài MVP scope", tạo bảng rỗng
>    vĩnh viễn không phải production.

### Database (AI DB)

- [x] Alembic migration: `raw_samples`
- [x] Alembic migration: `labeled_samples`
- [x] Alembic migration: `sample_splits`
- [x] Alembic migration: `provider_patterns` — *4 bảng trên gộp trong migration `0001_dataset` (ra đời cùng lúc và ràng buộc FK lẫn nhau — cùng lý do đã ghi ở Phase 1/2, tách ra sẽ thành migration rỗng). Tổng cộng 4 migration theo nhóm domain: `0001_dataset`, `0002_model_registry`, `0003_evaluation`, `0004_production_feedback`.*
- [x] Alembic migration: `model_versions`
- [x] Alembic migration: `training_jobs` — *gộp trong `0002_model_registry`. Điểm cốt lõi là partial unique index `uq_model_versions_active` trên `(stage) WHERE status='active'`: DB bảo đảm mỗi stage có ĐÚNG một model đang phục vụ — cơ chế promote nằm ở tầng lưu trữ chứ không phải quy ước trong code.*
- [x] Alembic migration: `evaluation_runs` + `evaluation_category_metrics` + `evaluation_predictions` — *`0003_evaluation`. Giữ từng dự đoán chứ không chỉ điểm tổng: accuracy 94% không cho biết nó hỏng ở danh mục nào, mà đó mới là thứ quyết định có promote hay không.*
- [x] Alembic migration: `pipeline_requests` — *`0004_production_feedback`. Bảng này **lưu** `amount_cents` — bắt buộc, vì Duplicate Detector (ARCHITECTURE.md §3.2) truy vấn chính cột đó; AGENTS.md §3.2 cấm **log** số tiền, không cấm lưu. Thêm `raw_sample_id` để phản hồi của người dùng lần ngược được về câu gốc, thiếu nó thì feedback không bao giờ thành nhãn huấn luyện được.*
- [x] Alembic migration: `user_feedback` + `feedback_batch_jobs` — *cùng `0004`.*
- [-] Alembic migration: `ab_experiments` + `ab_assignments` + `ab_metrics` — *Skipped: quyết định #4. A/B testing nằm ở Backlog, production tạo bảng khi tính năng ship.*
- [x] Seed: provider_patterns cho MB Bank, Vietcombank, MoMo, ZaloPay — *+ VNPay, 8 pattern trong `app/data/provider_patterns.json`, seed idempotent lúc khởi động (giống `ProviderConfigSeeder`), chỉ THÊM chứ không ghi đè — pattern đã sửa tay trong DB không bị một lần restart cuốn trôi.*

### AI Pipeline

- [x] `orchestrator.py` — điều phối stages — *Dựng response TỪ dòng `pipeline_requests` vừa ghi chứ không từ biến trong bộ nhớ, nên thứ backend nhận và thứ nằm trong AI DB không thể lệch nhau. Retry cùng `backend_request_id` trả lại kết quả cũ (`RetryFailedNotificationJob` gửi lại đúng request cũ — chạy lại pipeline sẽ tự dò trùng với chính dòng của mình).*
- [x] `stages/classifier.py` — Financial/Non-financial classifier — *Model nếu đã promote, không thì rơi về luật (từ khoá âm: OTP/khuyến mãi/bảo trì; dương: "GD:", "số dư", "thanh toán"…). `package_name='manual_entry'` BỎ QUA cổng phân loại — người dùng chủ động gõ câu chi tiêu thì ý định đã rõ, áp cổng vào đây chỉ chặn nhầm chính họ.*
- [x] `stages/extractor.py` — Amount, merchant, date extraction
  - [x] Rule-based extraction fallback per provider — *Mỗi pattern là MỘT regex phủ cả template với named group (`amount`/`sign`/`merchant`/`balance`/`occurred_at`), không phải một regex mỗi trường: tách rời thì hai template của cùng ngân hàng khớp chéo group của nhau và ghép ra giao dịch chưa từng tồn tại. Nhánh generic cho ngân hàng lạ tách số tiền khỏi SỐ DƯ (số dư gần như luôn lớn hơn, nên chọn theo giá trị lớn nhất là chọn đúng con số sai).*
  - [x] VND amount parser ("75k", "1.5tr", "75,000") — *+ "2tr5", "75k5", "1.234,56", "75,000.00". Phân biệt dấu ngăn nghìn với dấu thập phân theo ĐỘ DÀI nhóm cuối chứ không theo ký tự — tiếng Việt dùng `.` ngăn nghìn còn en-US dùng `,`, mà app ngân hàng viết cả hai kiểu; đọc nhầm "75.000" thành 75 sai 1000 lần mà vẫn là số hợp lệ.*
- [x] `stages/categorizer.py` — Category + confidence — *Model nếu đã promote, không thì từ điển 198 từ khoá → 11 slug hệ thống. Chỉ được trả slug trong 11 slug `CategorySeeder` đã seed: slug lạ làm `GetSystemBySlugAsync` trả NULL và giao dịch mất danh mục im lặng. `Credit` luôn là `income` theo luật — taxonomy không có danh mục thu nào khác nên hỏi model chỉ tạo cơ hội sai.*
- [x] `stages/duplicate_detector.py` — Dedup trong 5 phút — *Khớp theo `(user_id_hash, amount_cents, transacted_at ±5 phút)`, **KHÔNG lọc theo `package_name`**: ca trùng kinh điển là một lần quẹt thẻ sinh hai thông báo, một từ app ngân hàng một từ ví liên kết (docx Flow 1 bước 4.3) — lọc theo package sẽ bỏ sót đúng ca cần bắt. Chỉ so với dòng chưa bị đánh dấu trùng nên ba thông báo cùng một giao dịch đều trỏ về một bản gốc.*
- [x] `api/v1/pipeline.py` — POST /api/v1/analyze endpoint — *Lỗi không lường trước CỐ Ý thoát ra thành HTTP 500 để backend đánh dấu `failed` và `RetryFailedNotificationJob` thử lại; nuốt lỗi rồi trả 200 kèm `pipeline_result="error"` sẽ làm thông báo mất vĩnh viễn vì một sự cố DB vài giây.*
- [x] `api/v1/feedback.py` — POST /api/v1/feedback endpoint — *Danh mục TỰ TẠO của người dùng có slug tuỳ ý, ghi thẳng vào DB sẽ vi phạm CHECK constraint và làm request 500 — mà backend gọi feedback kiểu best-effort nên sẽ nuốt lỗi và không ai biết dữ liệu đang mất. Nay quy về NULL, giữ lại vế `predicted`.*
- [x] `utils/anonymizer.py` — SHA-256 hash, body anonymization — *Nằm ở `app/core/anonymizer.py` (đúng cây thư mục ARCHITECTURE.md §3.1, không phải `utils/`). Che số tài khoản/thẻ/SĐT/email nhưng GIỮ NGUYÊN số tiền — số tiền là nhãn của chính bài toán, che nhầm là xoá mất dữ liệu huấn luyện; đã loại trừ trường hợp số tiền viết liền không dấu ("2500000VND").*

### AI Training & Evaluation

- [x] `scripts/train.py` — training script — *TF-IDF trên n-gram KÝ TỰ (`char_wb` 2–5) chứ không phải từ: tiếng Việt trong thông báo xuất hiện cả có dấu lẫn không dấu, viết hoa toàn phần, dính ký hiệu — n-gram ký tự bắt được họ hàng giữa các biến thể mà không cần tách từ, nên đường phục vụ không cần `underthesea`. Model sinh ra ở trạng thái `candidate`, chưa phục vụ request nào.*
- [x] `scripts/evaluate.py` — evaluation script — *Chấm trên split `test`, ghi `evaluation_runs` + metrics theo từng nhãn + từng dự đoán.*
- [x] `scripts/feedback_batch.py` — convert feedback → labeled_samples — *Chỉ lấy lần sửa CUỐI của mỗi giao dịch: người dùng sửa đi sửa lại sẽ nhồi hai nhãn mâu thuẫn cho cùng một câu vào tập train.*
- *(Thêm ngoài danh sách gốc)* `scripts/seed_dataset.py`, `scripts/promote.py`, `scripts/generate_bootstrap_corpus.py`, `scripts/purge_raw_samples.py` — *`promote.py` là mắt xích còn thiếu giữa train và phục vụ: nó TỪ CHỐI promote model chưa được chấm trên split `test` hoặc có accuracy dưới 0.70. Không có cổng đó thì "train xong là dùng" và không ai biết model mới tốt hơn hay tệ hơn model cũ.*

### Tests

- [x] Unit test classifier với fixture notifications từ 5 providers
- [x] Unit test extractor: VND parser, date parser
- [x] Unit test categorizer
- [x] Integration test full pipeline flow — *`pytest` 104 test. Test tích hợp tự tạo một database riêng trên chính server Postgres của `docker compose` rồi xoá đi — không thêm dependency (`testcontainers` Python không có trong `TECH_STACK.md`); không có Postgres thì chúng SKIP kèm lý do, test đơn vị vẫn chạy. Có test hồi quy "mọi pattern `is_active` còn khớp `sample_text` của chính nó" — sửa regex làm hỏng template cũ sẽ đỏ ở CI thay vì lộ ra khi người dùng mất giao dịch.*

## Phase 10 — Đối chiếu Core User Flows (docx)

> Nguồn: `FinMate_Core_User_Flows.docx` đối chiếu với `.context/` ngày 2026-09-11.
> 4 quyết định đã chốt với user — xem bảng đầy đủ ở `ARCHITECTURE.md` §0.

### Quyết định #1 — Internal Transfer (Flow 2c)

- [x] Enum: `TransactionType` thêm `Transfer`
- [x] Entity: `Transaction.CounterAccountId` (ví đích; NULL với debit/credit)
- [x] Migration: `AddInternalTransfer` — cột `counter_account_id` + FK Restrict + index, mở rộng `chk_transactions_transaction_type`, thêm `chk_transactions_transfer_shape` buộc `transfer` ⇔ có `counter_account_id` khác `financial_account_id` và `category_id IS NULL` — *ràng ở DB chứ không chỉ validator: transfer thiếu ví đích sẽ trừ tiền ví nguồn mà không cộng vào đâu cả, tức mất tiền im lặng*
- [x] Command: `CreateTransferCommand` + Handler + Validator — *cả 2 ví đều đọc qua `userId`, ví của user khác trả 404*
- [x] Cascade balance 2 chiều: ví nguồn trừ, ví đích cộng
- [x] Loại `transfer` khỏi budget cascade — *`TransactionBudgetDelta.Spend` vốn chỉ trả > 0 cho `Debit`, nên `Transfer` rơi vào nhánh 0 sẵn; đã ghi chú tại chỗ để không ai "sửa" thành switch liệt kê thiếu*
- [x] Loại `transfer` khỏi mọi report query — *đã rà cả 6 điểm cộng dồn (`ReportRepository` ×4, `GetTransactionTimelineQueryHandler`, `TransactionRepository.SumConfirmedSpendAsync`); tất cả đã ở dạng `== Debit ? x : 0` hoặc filter `== Debit` nên loại `Transfer` tự động, verify bằng curl thật chứ không chỉ suy luận*
- [x] Update/Delete transfer: revert đúng cả 2 ví — *`UpdateTransactionCommandHandler` gom việc chạm số dư vào `ApplyBalanceAsync` có nhớ ví đã nạp, vì 1 lượt update có thể chạm cùng 1 ví nhiều lần*
- [x] Chặn đổi qua lại giữa `transfer` và `debit`/`credit` khi sửa (422 `TRANSACTION_TYPE_CHANGE_NOT_ALLOWED`) — *không có nhu cầu thật, mà mở ra thì phải revert theo một hình dạng rồi áp theo hình dạng khác*
- [x] Guard xóa `FinancialAccount`: `HasAnyForAccountAsync` xét cả `counter_account_id` — *ví chỉ từng đứng ở vế đích vẫn là ví đang có giao dịch*
- [x] Endpoint `POST /api/v1/transactions/transfer`
- [x] Tests: 5 unit (create) + 3 unit (update) + 2 unit (delete) + 5 integration

### Quyết định #2 — Ngưỡng cảnh báo ngân sách 70/90/100%

- [x] Migration: `AddBudgetAlert70And90` — `alert_80_sent_at` → `alert_70_sent_at`, thêm `alert_90_sent_at` — *EF tự đoán rename 80 → 90, ĐÃ SỬA TAY thành 80 → 70: dữ liệu cũ mang nghĩa "đã cảnh báo ở mốc thấp nhất", map sang 90 sẽ vừa nuốt mất cảnh báo 90% thật vừa bắn lại cảnh báo 70% cho chu kỳ đang ở 85%*
- [x] `BudgetAlertJob`: 3 mốc, chỉ gửi mốc CAO NHẤT đã chạm, đóng luôn các mốc thấp hơn — *nhảy vọt qua nhiều mốc giữa 2 lần chạy chỉ được 1 thông báo, không phải 3*
- [x] `UpdateBudgetLimitCommandHandler`: nâng hạn mức mở lại từng mốc độc lập
- [x] Cập nhật `BudgetAlertJobTests` cho 3 mốc (5 test) + `UpdateBudgetLimitCommandHandlerTests`

### Quyết định #3 — OTP

- [-] Đăng ký bằng SĐT + OTP — *Skipped: user chốt chưa cần trong MVP (2026-09-11). Giữ Email/Password + Google login. Docx bước 1.1 coi như overspec ở điểm này.* **ĐẢO MỘT PHẦN ở Phase 17 (2026-09-15):** user chốt làm OTP qua EMAIL. Phần SĐT/SMS vẫn bỏ — cần nhà mạng và tốn tiền thật.

### Quyết định #4 — Voice input + Receipt OCR (kéo từ Backlog lên MVP)

> **Ba điểm treo, tự chốt ngày 2026-09-13 sau khi user bảo tiếp tục:**
> 1. **OCR bằng Tesseract tự host** (`pytesseract` + `Pillow` + gói hệ điều hành
>    `tesseract-ocr-vie`). EasyOCR/PaddleOCR kéo `torch` ngược vào image PHỤC VỤ (~2.5GB),
>    xoá đúng việc tách requirements ở Phase 9; OCR đám mây thì đẩy ảnh hóa đơn của người
>    dùng ra bên thứ ba. Đã bổ sung vào `TECH_STACK.md` §2.2 kèm lý do.
> 2. **Không lưu ảnh** — xử lý trong bộ nhớ rồi bỏ. Docx chỉ yêu cầu hiển thị ảnh kèm thông
>    số để rà soát, mà client đang cầm sẵn tấm ảnh vừa chụp.
> 3. **Viết parser số đọc bằng chữ.** Docx phương thức 2 lấy ví dụ *"Cà phê Highland bốn
>    mươi lăm ngàn"* — số bằng CHỮ. Không có parser này thì chính câu mẫu của tài liệu ra
>    `uncertain`.

- [x] `TransactionSource` thêm `Voice` và `Receipt` — *migration `AddVoiceAndReceiptTransactionSources` nới `chk_transactions_source`. `CreateManualTransactionCommand` nhận thêm `Source` (mặc định `Manual` nên client cũ không hỏng), nhưng validator **TỪ CHỐI `Notification`**: đó là điều kiện lọc của tỉ lệ "người dùng sửa lại danh mục AI đoán" ở `/admin/ai-stats`, client khai bừa được thì thước đo chất lượng AI bị bóp méo bởi dữ liệu tự nhập mà không ai nhìn ra.*
- [x] Voice: STT chạy ở client (Android), text đẩy vào `POST /transactions/parse` sẵn có — *Đúng là không cần route AI Service mới, NHƯNG cần `app/utils/number_words.py`: STT trả về đúng những gì người dùng nói. Parser khớp trên chữ **CÓ DẤU**, không bỏ dấu — bỏ dấu làm **mười**(10) lẫn **mươi**(×10), **từ** lẫn **tư**(4), "công **ty**" lẫn **tỷ**, **làm** lẫn **lăm**(5); lỗi này bắt được khi "tháng chín mười lăm triệu" ra 95,4 triệu thay vì 15 triệu. Xử được "hai mươi mốt/tư/lăm", "rưỡi" (nửa của hàng vừa dùng), "một triệu hai" (chữ số lẻ sau đơn vị là phần mười). **Không tự nhân 1000** cho cụm không đơn vị: "hai trăm rưỡi" ra 250 chứ không phải 250.000 — đoán thêm một dấu nhân là đúng loại lỗi tệ nhất ở đây.*
- [x] OCR: thêm route `POST /api/v1/ocr` vào contract Backend↔AI Service (`ARCHITECTURE.md` §3.3) — *multipart, ảnh tối đa 5MB, chỉ JPEG/PNG/WebP/HEIC. `ocr_result` có ba giá trị `success`/`no_amount`/`unreadable` chứ không phải một cờ thành/bại: ba trạng thái dẫn tới ba lời nhắc khác hẳn nhau, gộp lại thì client chỉ còn cách nói "thử lại" cho cả ba. Response **không** trả text OCR thô.*
- [x] OCR: endpoint backend nhận ảnh hóa đơn → trả field trích xuất để client prefill — *`POST /api/v1/transactions/scan-receipt`. Không tạo giao dịch: docx yêu cầu rà soát trước khi lưu, nên giao dịch chỉ ra đời khi người dùng bấm Lưu qua `POST /transactions` với `source=Receipt`. Chốt chặn kích thước ở cả hai đầu và `[RequestSizeLimit]` ở tầng ASP.NET Core. Bóc tổng tiền theo **nhãn** ("Tổng cộng", "Tổng thanh toán"…) và loại trừ nhãn phản chỉ định ("Tiền khách đưa", "Tiền thối lại") — chọn con số lớn nhất trên hóa đơn siêu thị sẽ lấy nhầm tiền khách đưa, và sai luôn theo hướng lớn hơn thực tế. Nhãn đứng SAU thắng, vì hóa đơn liệt kê từ trên xuống. Categorizer chỉ nhận TÊN CỬA HÀNG, không nhận toàn văn: hóa đơn WinMart có dòng "Banh mi" phải là `shopping` chứ không phải `food`. `confidence` luôn < 0,85 — luồng này theo docx luôn có bước người xem lại nên không được rơi vào vùng xác nhận một chạm.*
- [x] Tests cho cả 2 kênh — *AI Service +48 test (104 → **152**): 25 test số đọc bằng chữ (gồm các bẫy chỉ xuất hiện khi bỏ dấu), 12 test bóc trường hóa đơn, 11 test HTTP `POST /ocr`. Backend +25 test (345 → **370**): guard kích thước/định dạng/kênh nhập và vòng đời quét hóa đơn. Engine OCR bị thay bằng fake trong test — nó là biên I/O phụ thuộc binary hệ điều hành, còn giá trị thật nằm ở phần đọc text; engine thật verify riêng bằng smoke test trên `docker compose`.*

## Phase 11 — Đối chiếu lại Core Flow 1 & 2 với docx

> Đọc lại TRỌN VẸN Flow 1 và Flow 2 (lần trước chỉ grep quanh vài từ khoá) lộ ra năm chỗ code
> chưa làm đúng đặc tả. `TASKS.md` báo đủ 258/261 vì nó đối chiếu với chính nó, không phải với
> docx — đó là loại sai lệch mà một bảng checkbox không bao giờ tự phát hiện được.

- [x] **Bước 1.2 — Endpoint danh sách app theo dõi** — *`GET /api/v1/financial-accounts/providers`. Trước đó KHÔNG có đường nào cho người dùng thường xem provider: client buộc phải gửi `providerConfigId` khi tạo ví mà không có cách nào biết id, nên màn onboarding không dựng được. Trả kèm `packageName` vì client Android dựng bộ lọc Tầng 1 (`sbn.packageName`) từ chính giá trị đó. Chỉ trả provider đang BẬT.*
- [x] **Bước 1.2 — Ba provider docx nêu mà hệ thống thiếu** — *Techcombank, VPBank, ShopeePay. Seed ở trạng thái **TẮT**: `package_name` của chúng chưa đối chiếu với thông báo thật, mà sai `package_name` là hỏng im lặng — provider hiện ra ở onboarding, người dùng chọn, rồi không thông báo nào khớp và không có gì báo lỗi. Admin bật lên qua `PATCH /admin/provider-configs/{id}/activation` sau khi kiểm chứng, đúng cơ chế dựng ở Phase 8. VNPay giữ nguyên dù docx không nêu — nó đang chạy và có pattern thật.*
- [x] **Bước 1.3 — Thu nhập hằng tháng** — *Migration `AddMonthlyIncomeToUsers`, `users.monthly_income_cents BIGINT NULL` (user chốt: một cột, không bảng lịch sử). Khác hẳn `TotalIncomeCents` trong báo cáo — cái đó là tổng giao dịch `Credit` THỰC TẾ và bằng 0 ở tháng đầu dùng app, đúng lúc người dùng cần cảnh báo bội chi nhất. NULL = chưa khai; gửi 0 qua `PATCH /users/me` để xoá.*
- [x] **Flow 3 mục 4 — Hành động đề xuất cho dự báo** — *`SpendingForecastDto.Advice`: thu nhập, số dư dự báo (âm = bội chi), và **số tiền nên cắt mỗi ngày**. Kịch bản nguyên văn docx có test: dự báo 10,2tr / thu nhập 9tr / còn 10 ngày → cắt 120.000đ/ngày. `null` khi chưa khai thu nhập — "chưa biết" khác "không bội chi". Tính ở `GetSpendingForecastQueryHandler` chứ không nhét vào `ISpendingForecaster`, để forecaster thuần thống kê và giữ nguyên chỗ cắm model AI.*
- [x] **Flow 3 bước 2.2 — Kiểm tra tính khả thi mục tiêu** — *`GoalProgressDto.Feasibility`: cần bao nhiêu/tháng, dư ra bao nhiêu (thu nhập − tổng hạn mức ngân sách), khả thi hay không, và hai hướng điều chỉnh docx nêu (kéo dài hạn / giảm mục tiêu). Là **cảnh báo kèm gợi ý, không từ chối**. Khác `IsOnTrack` sẵn có: cái kia hỏi "đang đi đúng nhịp chưa", cái này hỏi "nhịp đó có nằm trong khả năng tài chính không" — một mục tiêu mới tinh luôn đúng nhịp nhưng vẫn có thể bất khả thi ngay từ đầu.*
- [x] **Bước 5.4 — Tab "Chờ duyệt"** — *`GET /api/v1/transactions?status=Draft`. `GetTransactionListQuery` lọc được theo ví/danh mục/loại/khoảng ngày nhưng không có `Status`, nên client không tách được giao dịch nháp ra khỏi lịch sử.*
- [x] **Flow 2 mục 2a — Cảnh báo ngân sách TỨC THÌ** — *Chỗ lệch nặng nhất. Docx: "Mỗi khi có giao dịch phát sinh từ Flow 1 hoặc Flow 2, hệ thống tự động tính toán tỷ lệ phần trăm và phản ứng qua Mascot". Thực tế `BudgetAlertJob` chạy `0 * * * *` và **không ai đọc cờ `Alert*SentAt` ngoài job đó** — người dùng vượt 90% hạn mức chờ tới 59 phút. Tách logic mốc thành `BudgetAlertEvaluator` dùng chung; `BudgetPeriodService.ApplyDeltaAsync` nay TRẢ VỀ danh sách cảnh báo và 3 handler gửi chúng **SAU** `SaveChangesAsync` — gửi trong `ApplyDeltaAsync` là báo về một giao dịch có thể bị rollback, vì hàm đó cố ý không save. Job giữ nguyên làm lưới vét cho hai ca không có giao dịch: hạ hạn mức, và backfill lúc tạo budget. Tuỳ chọn thông báo được hỏi **trước** khi đóng cờ `Alert*SentAt`: đóng cờ cho người đã tắt cảnh báo là ăn mất mốc đó vĩnh viễn — họ bật lại hôm sau thì cờ đã nói "đã gửi" và không ai gửi lại.*
- *(Sửa kèm)* `GetMatchingBudgetsAsync` thiếu `Include(Category)` — cảnh báo tức thì lấy tên danh mục qua đó, thiếu thì MỌI cảnh báo đều ghi "toàn bộ chi tiêu" kể cả budget Ăn uống. Không lỗi, chỉ là thông điệp sai.

## Phase 12 — Push thật qua FCM

> `IPushNotificationService` đã hoàn chỉnh phần "gửi cho ai, khi nào, nội dung gì" từ Phase 4
> nhưng chỉ ghi log — server không có đường nào chạm tới điện thoại. User duyệt dependency
> ngày 2026-09-14.

- [x] **Duyệt `FirebaseAdmin` vào `TECH_STACK.md`** — *Không provider nào khác gửi được xuống Android khi app đã đóng: Android chỉ giữ MỘT kết nối thường trực cho cả máy và kết nối đó là của Google. Kéo theo `Google.Api.Gax.Rest` + `Google.Apis.Auth` (đã duyệt sẵn cho Google login).*
- [x] **Bảng `device_tokens`** — *Migration `AddDeviceTokens`. Một user nhiều máy; một token thuộc đúng MỘT user (`uq_device_tokens_token`). Đăng ký lại trên máy người khác thì **chuyển chủ**, không thêm dòng — để dòng cũ lại là thông báo tài chính của người mới vẫn đẩy xuống đúng máy đó cho người cũ đọc.*
- [x] **`POST` / `DELETE /api/v1/devices`** — *Token đi trong BODY cả ở `DELETE`, dù DELETE-có-body là hơi lạ: token FCM là capability, mà URL thì nằm trong access log và lịch sử proxy. Gỡ một token không tồn tại vẫn trả 204 — đăng xuất phải luôn thành công, và phân biệt "đã gỡ" với "chưa từng có" chỉ tổ cho biết token nào đang tồn tại.*
- [x] **`FcmPushNotificationService` + seam `IFcmSender`** — *`FirebaseMessaging` là API static không mock được, nên bọc một lớp mỏng chỉ dịch dữ liệu; toàn bộ phần đáng test nằm ở service. Dùng `SendEachForMulticastAsync` (không phải `SendMulticastAsync` đã deprecated) để biết ĐÍCH DANH token nào chết mà xoá.*
- [x] **Chốt chặn `PushEnabled` đặt ở service** — *Hai trong bốn chỗ gọi push (`AnalyzeNotificationCommandHandler`, `ContributeToGoalCommandHandler`) **không** kiểm cờ này. Vô hại khi mọi thứ chỉ ghi log; bật kênh thật lên là người đã tắt thông báo bắt đầu nhận. Kiểm ở service thì không call site mới nào quên được.*
- [x] **Chỉ xoá token FCM khẳng định đã chết** — *`Unregistered` và `SenderIdMismatch`. Mất mạng hay FCM quá tải là lỗi TẠM THỜI và thiết bị vẫn tốt — xoá token vì chúng là tự cắt đứt đường tới một máy còn sống, không gì khôi phục ngoài việc người dùng mở lại app.*
- [x] **Không bao giờ ném lỗi lên caller** — *Mọi chỗ gọi push đều đứng sau một giao dịch đã lưu. Ném ở đây là để FCM sập kéo theo việc ghi nhận chi tiêu thất bại.*
- [x] **Gửi không tới ai thì ghi `Warning` kèm mã lỗi** — *Ca thật khi sai credentials: FCM **không ném**, nó trả lỗi theo từng token; chỉ đếm "0 delivered" ở mức Information thì triệu chứng duy nhất là không ai nhận được gì. Mã lỗi lấy tên hằng + tên kiểu exception ở ĐẦU `Message` — không bao giờ lấy cả `Message`, vì khi lỗi thuộc về chính token thì FCM nhắc lại token trong đó.*
- [x] **Tự lùi về log-only khi thiếu credentials** — *Bắt buộc `FCM_CREDENTIALS_*` sẽ chặn mọi máy dev và mọi integration test không có Firebase project mà không bảo vệ được gì. Dòng log lúc khởi động nói rõ đang ở chế độ nào.*
- *(Sửa kèm)* `StatisticalSpendingForecaster` tính `basedOnDays` bằng số ngày đã qua trong tháng kể cả khi người dùng chưa nhập giao dịch nào — nên từ mùng 14 trở đi, một dự báo dựng trên hư không tự nhận "medium confidence", còn mùng 13 thì vẫn "low". Lộ ra vì test chạy sang ngày hôm sau. Độ tin cậy phải phản ánh lượng dữ liệu, không phải tờ lịch.

## Phase 13 — Đổi vai trò & giám sát yêu cầu xoá tài khoản

> Hai lỗ hổng VẬN HÀNH của phần quản trị, không phải lệch docx — docx không có flow admin nào,
> nên không có gì để đối chiếu và cũng không có gì báo là đang thiếu.

- [x] **`PATCH /api/v1/admin/users/{id}/role`** — *Trước đó đường DUY NHẤT tạo admin là `ADMIN_SEED_EMAIL`/`ADMIN_SEED_PASSWORD` đọc lúc khởi động; thêm admin thứ hai nghĩa là sửa env rồi khởi động lại cả service. Nhận VAI TRÒ mong muốn (không phải lệnh thăng/hạ) như `/lock`; gọi lại cùng giá trị thì không ghi audit.*
- [x] **Chặn tự đổi vai trò của chính mình** — *Kể cả chiều THĂNG cũng chặn: một admin tự nâng quyền cho mình thì audit log mất hẳn ý nghĩa "ai trao quyền cho ai".*
- [x] **Chặn hạ admin HOẠT ĐỘNG cuối cùng** — *Đếm `Role=Admin AND NOT IsLocked`; admin bị khoá không đăng nhập nổi nên không phải đường cứu. Verify cho thấy chốt này chỉ với tới được qua đúng một đường: admin vừa bị khoá nhưng access token còn hạn 15 phút — và đó chính là lúc nó cần nhất.*
- [x] **Chặn thăng tài khoản đang chờ xoá** — *Trao quyền admin cho một tài khoản 30 ngày nữa bị xoá cứng là vô nghĩa.*
- [x] **Hạ quyền thì thu hồi refresh token** — *Vai trò nằm TRONG access token và không tra lại DB mỗi request, nên hạ quyền không có hiệu lực ngay: token cũ vẫn ghi `Admin` tới hết TTL. Thu hồi refresh token kẹp cửa sổ leo thang đặc quyền ở đúng 15 phút. Thăng quyền KHÔNG thu hồi — `RefreshTokenCommandHandler` nạp lại user từ DB nên vai trò mới tự có ở lần refresh kế tiếp.*
- [x] **`GET /api/v1/admin/data-deletion-requests`** — *Hàng đợi xoá cứng chạy 03:00 mỗi ngày bằng `ExecuteDeleteAsync` mà trước đó KHÔNG ai nhìn thấy. Trả kèm email/tên (join `IgnoreQueryFilters` vì họ đã soft delete) — không có email thì danh sách chỉ là một đống GUID. `daysUntilHardDelete` tính sẵn ở server: đó là con số quyết định có phải xử lý gấp không, để mỗi client tự trừ là mỗi nơi làm tròn một kiểu.*
- [x] **`POST /api/v1/admin/data-deletion-requests/{id}/cancel`** — *Là đường cứu DUY NHẤT: `DeleteAccountCommandHandler` soft-delete người dùng ngay lúc họ gửi yêu cầu nên họ không đăng nhập lại được để tự huỷ. Huỷ phải đặt `user.DeletedAt = null` — thiếu bước đó thì yêu cầu biến khỏi hàng đợi nhưng người dùng vẫn không vào được, tức là huỷ mà không cứu được ai. Chặn huỷ khi đã `Processed`: dữ liệu không còn để khôi phục, nói thẳng thay vì giả vờ thành công.*
- [x] **Migration `AllowCancellingDataDeletion`** — *CHECK constraint `status IN ('pending','processed')` phải mở cho `'cancelled'`. `ProcessedAt` đổi Ý NGHĨA chứ không thêm cột: "lúc yêu cầu thôi ở trạng thái chờ", `Status` nói theo hướng nào — hai cột thời gian cho hai kết cục loại trừ nhau chỉ tổ phải nhớ đọc cột nào.*
- [x] **`AdminUserSeeder` sửa lại tài khoản seed, không chỉ tạo mới** — *Phát hiện khi verify thủ công, không test nào bắt được: seeder chỉ hỏi "email này có chưa", nên admin seed bị hạ quyền thì sửa env + khởi động lại cũng KHÔNG cứu được gì — đường cứu duy nhất chỉ tồn tại trên giấy. Nay mỗi lần khởi động bảo đảm nó thực sự dùng được (đúng vai trò, không bị khoá, không chờ xoá). Mật khẩu không đặt lại: đổi mật khẩu admin là chuyện bình thường, ghi đè mỗi lần khởi động sẽ âm thầm trả nó về giá trị trong env.*
- *(Chốt với user)* **Không** làm nút xoá cứng ngay: giám sát nghĩa là nhìn thấy và ngăn được, còn một nút xoá tức thì là bấm nhầm một lần mất sạch dữ liệu. **Không** báo cho người dùng khi huỷ: họ đang soft delete nên không thiết bị nào còn token hoạt động, mà hệ thống chưa có kênh email.

## Phase 14 — Chịu được đồng bộ offline, và bật lại rate limit

> Docx có dòng **"Mạng Offline"**: *"Lưu thông báo tạm thời vào Room Database cục bộ; tự động
> đồng bộ lên Cloud khi có mạng trở lại"* (Core Flow 1 & 2). Phần Room nằm ở `android/` — ngoài
> phạm vi repo này. Phần server phải chịu được cú đồng bộ đó thì thuộc về đây, và nó chưa chịu được.

- [x] **Chống trùng thông báo không còn giới hạn thời gian** — *`contentHash` đã gói sẵn `ReceivedAt` làm tròn phút nên hai dòng cùng hash là cùng một thông báo tại cùng một phút — cửa sổ thời gian vốn thừa. Tệ hơn, nó đo trên `CreatedAt` (lúc SERVER ghi), nên một thông báo xếp hàng offline gửi lại sau 5 phút sẽ lọt và tạo nháp trùng. Kiểm chứng: lùi `created_at` về 1 tiếng trước rồi gửi lại → vẫn cùng `logId`, 1 dòng log, 1 nháp.*
- [x] **Nhánh `Ignored` GIỮ cửa sổ 5 phút** — *Cố ý khác nhánh trên. Thông báo bị bỏ qua vì ví chưa được theo dõi; người dùng thêm ví rồi app mới đồng bộ thì nó phải được xử lý LẠI chứ không bị nuốt vĩnh viễn. Cửa sổ ở đây chỉ để chặn một cú bắn dồn.*
- [x] **Kiểm tra trùng chuyển lên TRƯỚC khi tra ví** — *Để cả nhánh `Ignored` cũng được bảo vệ; trước đó nó nằm sau nên thông báo của ví chưa theo dõi không có lớp chặn nào.*
- [x] **`transactions.client_request_id` + unique một phần** — *Giao dịch nhập tay không có gì để băm: hai ly cà phê 45.000đ cùng quán trong cùng một phút là hai giao dịch THẬT, chỉ client mới biết đâu là gửi lại. Id do client sinh; `UNIQUE (user_id, client_request_id) WHERE client_request_id IS NOT NULL` — một phần vì nếu không thì mọi dòng NULL đụng nhau, và đây mới là thứ chặn được hai request gửi lại CÙNG LÚC (kiểm ở tầng ứng dụng vẫn có khe hở giữa đọc và ghi, mà hàng đợi offline thì bắn cả loạt).*
- [x] **`POST /transactions` và `/transactions/transfer` trả 200 khi là gửi lại** — *201 cho một request không tạo ra gì là nói sai với client, và client nào đếm số giao dịch đã đồng bộ theo mã 201 sẽ đếm nhầm. `clientRequestId` TUỲ CHỌN — client cũ không gửi vẫn chạy bình thường.*
- [x] **Gắn rate limit vào endpoint** — *Hai policy đã tồn tại từ Phase 0 nhưng **không gắn vào đâu cả**: không `[EnableRateLimiting]`, không `RequireRateLimiting`, không `GlobalLimiter`. `UseRateLimiter()` vẫn chạy nhưng không chặn gì, nên mức 10/phút cho đăng nhập hoàn toàn không có tác dụng chống dò mật khẩu.*
- [x] **Hạn mức mặc định phân vùng theo user id, không phải IP** — *Theo IP là sai ở cả hai đầu: cả một trường học sau một NAT dùng chung hạn mức, còn một tài khoản đổi mạng liên tục thì thoát. Nó cũng đánh nhầm đúng thứ đang cần hỗ trợ — hàng đợi offline đồng bộ một loạt request của CÙNG một người. Đòi `UseRateLimiter()` phải chạy SAU `UseAuthentication()`, nếu không `context.User` còn rỗng.*
- [x] **Mặc định đi qua `GlobalLimiter`, không qua `RequireRateLimiting`** — *Phát hiện khi verify: gắn policy mặc định lên `MapControllers()` ĐÈ MẤT `[EnableRateLimiting(AuthPolicy)]` trên endpoint đăng nhập — đo thực tế thấy `/auth/login` chạy ở mức 120 thay vì 10. `GlobalLimiter` thì cộng dồn, và một controller thêm sau này không thể lọt lưới.*
- [x] **`refresh` cố ý KHÔNG vào nhóm hạn mức chặt** — *Refresh token là chuỗi ngẫu nhiên nên dò không có ý nghĩa, mà siết nó sẽ chặn nhầm nhiều người dùng chung một NAT.*
- *(Không làm)* **Endpoint gửi theo lô.** Sau hai mục trên thì 50 request tuần tự đã ĐÚNG, chỉ chậm. Cái đáng làm là gộp lời gọi AI — mà đó là đổi contract Backend↔AI-Service, thuộc diện phải hỏi user theo `AGENTS.md` §5.

## Phase 15 — Nhánh 85%: xác nhận một chạm và hộp thoại chọn danh mục

> Phần CUỐI CÙNG còn thiếu của Core Flow 1. AI đã trả `confidence` từ Phase 9 và FCM gửi thật
> từ Phase 12, nhưng backend chưa dùng con số đó — mọi nháp đều nhận cùng một dòng chữ cụt,
> không kèm dữ liệu nào để client dựng nút xác nhận.

- [x] **Chọn nhánh bằng `min(extraction, categorization)`** — *Một chạm chốt TOÀN BỘ bản ghi (số tiền, loại, danh mục) nên mắt xích yếu nhất phải quyết định: bóc số 0,70 kèm phân loại 0,95 mà cho một chạm là chốt nhanh một con số TIỀN có thể sai. Không tính điểm Classifier — nó trả lời "đây có phải giao dịch không", một cổng đã đi qua rồi mới có nháp. `null` ở một trong hai → nhánh hộp thoại; dữ liệu vắng mặt không bao giờ được quy ra "chắc chắn".*
- [x] **Ngưỡng 85%, đặt ở một hằng số** — *Docx TỰ MÂU THUẪN: bước 5.2/5.3 ghi 85%, bảng tình huống biên ghi 80% cho đúng hành vi đó. User chốt 85% theo bản Step-by-Step.*
- [x] **Nâng điểm trúng từ điển 0,80 → 0,90 ở `categorize_by_rules`** — *Điều kiện để tính năng chạy được. Đo thực tế: ở ngưỡng 85% với điểm 0,80, mọi khoản CHI đều rớt xuống nhánh hộp thoại và chỉ giao dịch THU (0,95) được một chạm — trong khi ví dụ của chính docx ở bước 5.2 là một khoản chi tại Highlands Coffee. 0,80 đặt từ Phase 9, trước khi tồn tại ngưỡng nào để đối chiếu; trúng từ điển là khớp CHÍNH XÁC tên cửa hàng nên 0,90 mới đúng mức tin cậy thật. Sửa ở đây chứ không hạ ngưỡng: ngưỡng là quy định nghiệp vụ.*
- [x] **`data` payload trong push** — *`IPushNotificationService.NotifyAsync` thêm tham số `data` tuỳ chọn, xuyên xuống `MulticastMessage.Data`. Chỉ có chữ thì client biết CÓ giao dịch mới nhưng không biết xác nhận cái nào — nút một chạm không dựng được. Payload: `type`, `action`, `transactionId`, `amountCents`, `transactionType`, `merchantName`, `categorySlug`.*
- [x] **`data` KHÔNG BAO GIỜ được ghi log** — *Nó mang số tiền dưới dạng bóc ra được bằng máy, cùng luật với `notification_body` ở CLAUDE.md. `FcmPushNotificationService` chỉ log số lượng và mã lỗi; bản log-only chỉ log KHOÁ của data để còn kiểm client nhận đủ trường.*
- [x] **Hai câu khác nhau cho hai nhánh** — *Một chạm dùng đúng câu docx: `"Bạn vừa chi 75.000đ tại Highlands Coffee (Ăn uống)?"`. Nhánh kia KHÔNG nêu danh mục — đọc lên một danh mục như thể đã chốt sẽ khiến người dùng gật theo thay vì chọn lại, mà chọn lại mới đúng là việc cần. Phân biệt chi/nhận theo `TransactionType`.*
- [x] **`POST /transactions/{id}/confirm` nhận `categoryId` tuỳ chọn** — *Để bước 5.3 thật sự là "1 chạm". Trước đó endpoint không nhận body nào nên đổi danh mục phải gọi `PUT` rồi mới `confirm` — hai lần gọi mạng cho một thao tác, và có khoảng giữa hai lần đó mà trạng thái lỡ dở. Gán danh mục TRƯỚC `ApplyDeltaAsync`: hạn mức phải trừ vào danh mục người dùng CHỌN, không phải danh mục AI đoán — đặt sau là tiền vào sai ngân sách mà không có gì báo.*
- [x] **Đổi danh mục thì dạy lại AI** — *`SendFeedbackAsync(..., "category_correction")`. Bảng tình huống biên docx: "lưu lại lựa chọn của người dùng để cải thiện thuật toán sau này" — người dùng vừa sửa đúng cái AI đoán sai, tín hiệu quý nhất có được.*
- *(Sửa kèm)* Số tiền trong push hiển thị `75,000đ` kiểu Mỹ thay vì `75.000đ` kiểu Việt — `:N0` trần lấy culture của tiến trình, mà container chạy `InvariantGlobalization`. Dựng `NumberFormatInfo` tay chứ không gọi `CultureInfo.GetCultureInfo("vi-VN")`: culture đó KHÔNG TỒN TẠI lúc chạy ở chế độ invariant và sẽ ném ngay khi khởi tạo lớp. Lỗi có sẵn từ dòng push cũ, test mới bắt được.

## Phase 16 — Bốn thứ chặn cứng việc chạy production

> Phần lõi đã vững, nhưng phần VẬN HÀNH vẫn là môi trường phát triển. Một trong bốn chỗ là lỗi
> tự tôi đưa vào ở Phase 14.

- [x] **Dockerfile multi-stage** — *Bản cũ `FROM sdk` + `dotnet run`: đóng gói cả SDK lẫn mã nguồn, **build lại mỗi lần container khởi động**, chạy Debug, chạy bằng **root**. Nay `publish -c Release` sang ảnh `aspnet`, `USER $APP_UID`. Đo được: **2,86GB → 359MB**, và `id` trong container trả `uid=1654(app)`.*
- [x] **`Directory.Build.props` COPY trước `restore`** — *`TargetFramework` không nằm trong bất kỳ `.csproj` nào mà ở file props dùng chung; chỉ copy `.csproj` là restore hỏng theo kiểu rất khó truy. Restore đứng riêng một tầng để sửa code không làm mất cache.*
- [x] **`backend/.dockerignore`** — *Trước đó KHÔNG có, nên `COPY . .` kéo cả `bin/`, `obj/`, `logs/`, và cả `.env` lẫn `secrets/` vào build context. `obj/` sinh trên host mang dấu vết đường dẫn và phiên bản SDK của máy host, đủ để làm hỏng build trong container.*
- [x] **Serilog chỉ ghi file ở Development** — *Ghi log vào ổ đĩa trong container thì không ai đọc được và đầy dần; production thu log từ stdout. Thêm lý do cụ thể: image chạy non-root và `/app` không cho user đó ghi.*
- [x] **Chốt chặn bí mật mẫu lúc khởi động** — *Ngoài Development, `JWT_SECRET`/`AI_SERVICE_API_KEY`/`ADMIN_SEED_PASSWORD`/`HANGFIRE_DASHBOARD_PASS` còn chứa `change-me` thì **từ chối khởi động**, nêu đủ tên các biến trong một lần. Giá trị đó nằm công khai trong `.env.example` ĐÃ COMMIT — một `JWT_SECRET` còn nguyên nghĩa là ai đọc repo cũng tự ký được token admin, mà mọi thứ vẫn chạy bình thường nên không có triệu chứng gì. Sinh secret một lần là chưa đủ: quên đổi ở lần triển khai sau là chuyện thường.*
- [x] **`backend/secrets/` vào `.gitignore` + sinh `JWT_SECRET` ngẫu nhiên** — *`.gitignore` phải có TRƯỚC khi tải file Firebase về: file đó gửi được thông báo cho toàn bộ người dùng, và commit nhầm thì phải thu hồi khoá chứ không xoá lịch sử được.*
- [x] **`UseForwardedHeaders` cấu hình qua `TRUSTED_PROXIES`** — *Sửa lỗi tôi đưa vào ở Phase 14. Sau reverse proxy thì `RemoteIpAddress` là IP của PROXY, mà rate limiter phân vùng đăng nhập theo IP — nghĩa là cả hệ thống dùng chung một ngăn 10 lần/phút. Mặc định TRỐNG = không bật, giữ nguyên hành vi cũ và an toàn: tin header khi chưa ai bảo tin là để client tự bịa IP thoát rate limit.*
- [x] **`/health/live` tách khỏi `/health/ready`** — *`/health` cũ trả `healthy` kể cả khi Postgres và Redis đã chết → load balancer tiếp tục đẩy request vào instance hỏng. Hai `IHealthCheck` viết TAY, không thêm package: `AspNetCore.HealthChecks.NpgSql/.Redis` là dependency mới phải xin duyệt (AGENTS.md §5) trong khi thứ cần chỉ là một vòng gọi. Tách hai đường vì hành động khác nhau: liveness hỏng thì restart container, readiness hỏng thì ngừng đẩy traffic — gộp làm một là Postgres sập kéo cả đàn app restart vô ích, mà restart không cứu được Postgres.*

## Phase 17 — OTP qua email: xác minh đăng ký và quên mật khẩu

> Hai lỗ hổng thực dụng trong phần auth. Đây là **đảo một phần** quyết định #3 (bỏ OTP,
> 2026-09-11) — lần đó là SĐT + SMS, lần này là email, rẻ hơn hẳn và không cần nhà mạng.

- [x] **Quên mật khẩu** — *Trước đó CHỈ có `change-password` và nó đòi đang đăng nhập, nên quên mật khẩu là mất tài khoản vĩnh viễn. Nay `forgot-password` + `reset-password` qua mã OTP.*
- [x] **Xác minh email lúc đăng ký** — *`users.email_verified_at`. Trước đó ai cũng đăng ký được bằng email của người khác. Migration **backfill mọi user đang có thành đã xác minh** — không backfill là khoá toàn bộ người dùng hiện tại ra ngoài, họ đăng ký từ trước khi tính năng tồn tại nên không có cách nào xác minh ngược.*
- [x] **`forgot-password` không bao giờ lộ email có tồn tại** — *Trả cùng một phản hồi (204, body rỗng) cho email thật lẫn email bịa, và nuốt cả nhánh "đang trong thời gian chờ". Endpoint ẩn danh mà phân biệt được là dò được cả danh sách người dùng. Kiểm bằng curl: hai phản hồi giống nhau từng byte.*
- [x] **Giới hạn gửi theo EMAIL, không chỉ theo IP** — *1 mã/60 giây cho mỗi cặp (email, mục đích). Hạn mức `auth` 10/phút theo IP không chặn được việc nhắm vào MỘT hộp thư cụ thể, mà mỗi lần gửi vừa tốn tiền vừa là rác trong hộp thư người khác.*
- [x] **Mã dùng một lần, hết hạn 10 phút, tối đa 5 lần thử** — *Đây mới là thứ bảo vệ thật. Băm SHA-256 chỉ để mã không nằm nguyên dạng trong dump hay log: mã 6 chữ số chỉ có 10⁶ khả năng nên ai cầm được dump sẽ dò ngược trong vài giây dù băm bằng gì. Sai quá 5 lần thì mã thật cũng chết theo — nếu không, kẻ dò chỉ cần xin mã mới rồi dò tiếp trên mã cũ.*
- [x] **Mã tách theo mục đích** — *Mã xác minh KHÔNG đặt lại được mật khẩu. Thiếu tách biệt thì ai chạm được vào hộp thư một lần là đổi được cả hai.*
- [x] **Đặt lại mật khẩu thu hồi toàn bộ refresh token** — *Lý do người ta đặt lại mật khẩu thường là "tôi nghĩ có người vào được tài khoản"; để phiên cũ sống tiếp là không giải quyết đúng điều đó. Cùng luật mật khẩu với lúc đăng ký — nới lỏng ở đây là làm luật kia thành vô nghĩa vì ai cũng đi đường vòng.*
- [x] **Brevo gọi bằng `HttpClient` trần, KHÔNG thêm package** — *API chỉ là một POST JSON kèm header `api-key`. Kéo cả SDK về cho đúng một lời gọi là thêm dependency phải duyệt (AGENTS.md §5) đổi lấy rất ít; đổi nhà cung cấp sau này chỉ là viết lại một lớp. Thiếu key thì tự lùi về bản in log, giống hệt FCM, nên máy dev và bộ test vẫn lấy được mã.*
- [x] **`REQUIRE_EMAIL_VERIFICATION` bật/tắt được** — *Mặc định BẬT. `AuthApiFactory` tắt vì **23 file test** gọi `auth/register` rồi đăng nhập ngay; bắt tất cả diễn lại màn OTP chỉ tạo nhiễu. `OtpAuthControllerTests` bật lại cờ trong phạm vi riêng bằng `WithWebHostBuilder` — không đụng biến môi trường dùng chung, vì cả bộ test tích hợp nằm chung một tiến trình.*
- *(Kèm theo)* Google login đặt luôn `email_verified_at` ở cả hai nhánh (liên kết tài khoản sẵn có và tạo mới) — Google đã xác minh hộ rồi, bắt làm lại là bắt làm lại đúng việc vừa xong. `BREVO_API_KEY` thêm vào `StartupSecretGuard` của Phase 16.

## Phase 18 — Google login qua trình duyệt nhúng (không cần SHA-1)

> Google login **đang hỏng**: `GOOGLE_CLIENT_ID` còn là `change-me-...` nên mọi token thật đều
> trượt kiểm `Audience`, mà 520 test không bắt được vì `AuthApiFactory` thay verifier bằng bản
> giả. Đường native còn đòi app đăng ký package name + SHA-1 — chưa có dự án Flutter dựng xong
> lẫn keystore từ đội mobile.

- [x] **`GET /auth/google/start`** — *302 sang `accounts.google.com` kèm `state`. `prompt=select_account` để Google luôn hiện màn chọn tài khoản: trên máy dùng chung, im lặng đăng nhập lại người trước là một bất ngờ khó chịu.*
- [x] **`state` chống giả mạo, dùng một lần** — *Thiếu nó thì kẻ tấn công dựng được callback bằng `code` của CHÚNG, khiến nạn nhân đăng nhập vào tài khoản Google của kẻ tấn công mà không biết. Lưu Redis, TTL 10 phút, huỷ ngay khi dùng.*
- [x] **`GET /auth/google/callback`** — *Đổi code lấy `id_token` rồi **đưa thẳng vào `IGoogleLoginCommandHandler` đã có**. Toàn bộ phần tra/tạo người dùng và phát JWT không viết lại một dòng.*
- [x] **Mã bàn giao thay vì token trong URL** — *Redirect chỉ mang một mã dùng một lần, TTL 2 phút. Nhét token thẳng vào deep link là để chúng rơi vào lịch sử trình duyệt, và trên Android **app khác đăng ký trùng scheme sẽ nhận được cùng đường link** — refresh token sống 30 ngày mà lọt kiểu đó là mất tài khoản. Có test ghim việc URL không bao giờ chứa `accessToken`/`refreshToken`/`eyJ`.*
- [x] **`POST /auth/google/exchange`** — *App đổi mã lấy `AuthResultDto`, đúng khuôn `ApiResponse` như mọi API khác.*
- [x] **Chế độ thử không cần app** — *`MOBILE_DEEP_LINK` trống → callback hiện mã ra một trang HTML tối giản. Đây là thứ khiến kiểm chứng được TOÀN BỘ luồng bằng trình duyệt máy tính, không cần Flutter, không cần SHA-1, không đợi đội mobile.*
- [x] **Thiếu cấu hình thì tắt, không sập** — *Đo thật: `/start` trả **422** kèm `AUTH_GOOGLE_OAUTH_NOT_CONFIGURED`, không phải 500, và `POST /auth/google` (đường native) vẫn chạy. Cùng khuôn với FCM và Brevo.*
- [x] **`GOOGLE_CLIENT_ID` + `GOOGLE_CLIENT_SECRET` vào `StartupSecretGuard`** — *Trước đó `GOOGLE_CLIENT_ID` **không** nằm trong danh sách, nên deploy với `change-me-...` thì app vẫn khởi động bình thường và Google login hỏng im lặng cho tới khi có người thật bấm nút — đúng loại hỏng mà chốt chặn đó sinh ra để chặn.*
- *(Giữ nguyên)* `POST /auth/google` không bỏ. Nó không vướng gì, và khi đội Flutter muốn dùng đường native thì đã có sẵn.

## Phase 19 — OCR hóa đơn qua `ocr-service` (PaddleOCR + VietOCR) thay cho Tesseract

> OCR trong `ai-service` dùng Tesseract nên đọc kém ảnh chụp thật (số bị cắt xuống dòng, chữ dính nhau). Bản
> PaddleOCR + VietOCR kèm luật bóc tên/tổng tiền/danh mục đã chạy riêng và đo trên bill thật. `TECH_STACK.md` đã
> loại PaddleOCR khỏi image của `ai-service` (kéo thư viện nặng), nên nó chạy thành một dịch vụ riêng.

- [x] **Thư mục `ocr-service/`** — *Chép từ bản chạy riêng, bỏ phần chat/Rasa, bỏ dữ liệu cá nhân (`dataset.jsonl`, `merchant_memory.json`) và ảnh bill thật của benchmark. 384 test pass. Nạp trước PaddleOCR và VietOCR lúc khởi động (`warm_up`): đo thật, lần quét đầu sau khi khởi động từng mất quá 30 giây và bị `ReadTimeout` ở AI Service (503). Model ONNX (~86MB) không nằm trong git, đặt tay vào `models/vietocr/`, xem `ocr-service/README.md`.*
- [x] **Khóa nội bộ `OCR_SERVICE_API_KEY`** — *`Authorization: Bearer`, cùng kiểu `INTERNAL_API_KEY`. Chưa đặt khóa thì từ chối hết (503), không mở. `/health` vẫn mở cho healthcheck. 4 test.*
- [x] **`docker-compose.yml`: service `ocr-service`** — *Không mở cổng ra máy chủ; `ai-service` gọi qua `OCR_SERVICE_URL=http://ocr-service:8000`.*
- [x] **`ai-service`: `remote_ocr.py` + route `/api/v1/ocr`** — *Có `OCR_SERVICE_URL` và `OCR_SERVICE_API_KEY` thì dùng ocr-service, không thì vẫn Tesseract. **Contract Backend ↔ AI Service không đổi**: backend và app không phải sửa gì. Ngày giao dịch vẫn do `_find_date` bóc từ các dòng chữ đã lọc. `confidence` vẫn luôn < 0,85, và ≤ 0,50 khi ocr-service đánh dấu cần xem lại. ocr-service lỗi thì 503, không trả đáp án sai. 12 test đơn vị + 4 test tích hợp.*
- [x] **Quy đổi danh mục sang slug backend** — *`SLUG_BY_OCR_CATEGORY`: dining/groceries→food, utilities→bills, healthcare→health, còn lại cùng tên. Mọi slug được test là thuộc `CATEGORY_SLUGS`. **Quyết định tạm, chờ xác nhận:** siêu thị (groceries) quy về `food`, nhóm "Khác" quy về `other`. ocr-service không biết cửa hàng thì rơi về `Categorizer` cũ.*
- [x] **Kiểm thử thật trên `docker compose`** — *5 ảnh bill thật qua ai-service → ocr-service: số tiền đúng cả 5 (1.305.000 / 37.200 / 16.000 / 888.929 / 739.000). Ngày giao dịch hay bị `null`: OCR dính chữ ("Ngay11-04-202613:16") nên `_find_date` không đọc được; bóc ngày ngay trong ocr-service là việc nên làm tiếp.*
- [ ] **Gỡ Tesseract** — *Chưa gỡ: cần duyệt vì sửa `TECH_STACK.md` (`AGENTS.md` §5). Gỡ gồm `receipt_ocr.py` phần engine, `pytesseract`, hai gói OS trong `ai-service/Dockerfile`, và cập nhật `TECH_STACK.md`/`CLAUDE.md`.*
- [x] **DB cho ocr-service: `postgres-ocr`** — *DB riêng (`finmate_ocr`), KHÔNG đụng schema của `finmate_main` lẫn `finmate_ai`: schema của AI Service do alembic quản lý và có ràng buộc riêng (11 slug, nguồn mẫu cố định), còn ocr-service dùng 9 nhóm và các bảng riêng. Đã khôi phục TOÀN BỘ dữ liệu từ DB dev cũ bằng `pg_dump`/`pg_restore`: 741 bill có nhãn (47 bạn xác nhận, 16 bộ chuẩn, 18 nhập từ file cũ, 660 giả lập), 61 dòng bộ nhớ cửa hàng, 396 thương hiệu, 492 từ khóa, 4 bill lạ, 5 giao dịch chat thử, và lịch sử huấn luyện (5 job, 4 model, 4 lần đánh giá). Kiểm chứng: `PgMerchantMemory` đọc đúng ("Lb eyewear" → healthcare cho người dùng cũ), từ khóa `eyewear` có trong bảng. Bản sao lưu: ngoài repo, `Model AI/_backup/`. **Hạn chế:** bộ nhớ cửa hàng theo `user_id_hash`; người dùng BE có hash khác nên bộ nhớ cũ (của hash dev `0123…` và `local`) chưa áp cho họ, cần quyết định gán hoặc chia sẻ làm nền.*

## Phase 20 — Chat qua Rasa: AI Service chỉ HIỂU câu, backend lưu giao dịch

> Bản chat chạy riêng (Rasa + engine) tự lưu giao dịch vào DB của nó, xử lý xác nhận/hoàn tác/sửa danh mục trong RAM. Backend đã có
> bảng giao dịch, bản nháp và `POST /transactions/{id}/confirm` nên chỗ lưu là backend. AI Service chỉ đọc câu và trả quyết định.

- [x] **Service `rasa` trong `docker-compose.yml`** — *Image `rasa/rasa:3.6.21`, không mở cổng ra máy chủ, model `finmate-nlu-v3` (93,2% ý định trên 74 câu giữ riêng). Cấu hình, dữ liệu huấn luyện và bộ đo ở `ai-service/rasa/`. **Phụ thuộc mới ngoài `TECH_STACK.md`** (một image Docker, không phải thư viện Python của ai-service): cần duyệt rồi ghi vào `TECH_STACK.md`.*
- [x] **`POST /api/v1/chat/parse`** — *Câu chat → `intent` + `entry` (loại, `amount_cents`, `category_slug`, mô tả, cửa hàng, `auto_save_ok`) hoặc `query` (loại tổng, kỳ, danh mục). Không lưu gì, không giữ trạng thái giữa các câu; hoàn tác, sửa danh mục, xác nhận và tính tổng là việc của backend. Khóa nội bộ như các route khác. Không ghi nội dung câu hay số tiền vào log. 45 test (parser và route). **Đây là route mới trong contract Backend ↔ AI Service: cần duyệt rồi thêm vào `ARCHITECTURE.md` §3.3.***
- [x] **`app/chat/`** — *Bộ đọc số tiền lóng (k, cành, củ, lít, rưỡi, "3 củ 8"), nhận loại giao dịch, đọc câu hỏi tổng, và phân xử giữa Rasa và luật: Rasa chỉ thắng khi chắc từ 0,97 nếu bất đồng loại giao dịch; câu có từ khóa chi tiêu rõ mà không có dấu hiệu thu/chuyển thì là chi tiêu dù Rasa chắc đến đâu. `auto_save_ok`: chi tiêu, đã biết danh mục, dưới 2.000.000đ (`CHAT_AUTO_SAVE_BELOW`), và Rasa (nếu có) chắc từ 0,9 đó là chi tiêu.*
- [x] **Quy đổi danh mục dùng chung với OCR** — *`app/core/category_map.py`. Thu nhập luôn là slug `income` kèm `income_type` (lương, thưởng...); chuyển tiền không có danh mục. Từ vựng chat không đoán được thì thử từ điển cửa hàng của `Categorizer` (chỉ nhận khớp chính xác).*
- [x] **Kiểm thử thật** — *Trên `docker compose` với Rasa thật: "đi ăn 200k" → food, auto; "cắt kính cận 800k" → health (Rasa nói thu nhập 1,0, luật thắng); "nhận lương 14 củ 5" → income/salary; "chuyển 2 triệu sang tiết kiệm" → transfer; "mua áo 3 triệu" → shopping, không auto (từ 2 triệu); "tháng này tôi tiêu bao nhiêu" → query expense/tháng này; câu hỏi "iphone 20 triệu có đáng mua không" → out_of_scope. Toàn bộ test của ai-service: 243 pass.*
- [x] **Phía backend C#: `POST /api/v1/transactions/chat`** — *`ChatCommandHandler` gọi `IAIServiceClient.ParseChatAsync` rồi quyết định. Trả `action`: `saved` (đã ghi, kèm giao dịch), `confirm` (bản nháp + `missingFields`), `summary` (số liệu), hoặc `reply`. **Chỉ ghi thẳng khi AI nói `auto_save_ok` VÀ backend tự chọn được ví (ví người dùng chọn, hoặc ví duy nhất) VÀ tìm được danh mục hệ thống theo slug** — thiếu một trong ba thì trả bản nháp, vì ghi giao dịch không danh mục hay vào nhầm ví là sai lặng lẽ. Thu nhập và chuyển tiền luôn qua xác nhận (chuyển tiền cần hai ví). `source = Manual` như câu gõ tự nhiên (docx), không thêm giá trị enum nên không có migration. Hoàn tác và sửa danh mục dùng `DELETE`/`PATCH /transactions/{id}` có sẵn. Tổng chi/thu/còn lại tính từ `IReportRepository` của chính người dùng, không nhờ AI tính. 15 test đơn vị + 7 test tích hợp (Postgres thật): ghi → trừ số dư → hoàn tác trả lại đúng số dư, không ví thì chỉ có bản nháp, tổng không lẫn giao dịch của người khác.*
- [ ] **Tin nhắn chưa rõ (khi người dùng đồng ý chia sẻ)** — *Chưa chuyển: bản cũ lưu vào bảng `chat_unclear_messages`; AI DB mới cần alembic migration (cần duyệt đổi schema). Route đã trả `unclear_reason` (low_confidence/disagree/not_understood) để backend hoặc bước này dùng sau.*
- [ ] **Bộ nhớ cửa hàng của từng người** — *Bản cũ nhớ "cửa hàng X người dùng đã chốt là danh mục Y"; ở bản này việc đó thuộc về backend (có `category_correction`).*

## Phase 21 — Tách bước phân loại hóa đơn sang AI Service (luồng: Ảnh → OCR → lọc → AI phân loại → BE)

> Hiện `ocr-service` làm cả OCR, lọc, bóc tách **và phân loại** trong một lần gọi, còn `ai-service` chỉ đổi định dạng. Đích: `ocr-service` đọc và lọc,
> BE điều phối và lưu kết quả bóc tách, `ai-service` chỉ nhận diện và phân loại (giống Rasa). Làm theo bốn bước (a)→(d), mỗi bước chạy thử xong mới sang bước sau.

- [x] **(a) `POST /api/v1/bill/classify`** — *Nhận tên cửa hàng + các dòng chữ ĐÃ lọc (không nhận ảnh hay chữ gốc), trả `category_slug` (slug backend, hoặc null khi không nhận ra), `local_category` (9 nhóm, giữ phân biệt Ăn uống/Siêu thị), `confidence` (trần 0,80), `source` (brand/keyword/product/none). Không trạng thái, không ghi DB, không ghi chữ vào log. Logic chép đúng `classify_category_detailed` của ocr-service (`app/bill/`). **Đối chiếu trên 741 bill thật trong DB cũ: giống ocr-service 741/741.** Khớp nhãn người dùng chốt: 62/81 (77%) bill thật chỉ với tên + dòng chữ (chưa có bộ nhớ cửa hàng theo người dùng, chưa có model), 652/660 (99%) bill giả lập. 18 test mới; chạy thật trong container. Route mới trong contract Backend ↔ AI Service: cần duyệt rồi ghi vào `ARCHITECTURE.md` §3.3.*
- [x] **(b) BE gọi tuần tự `ocr-service` rồi `ai-service`** — *Có `OCR_SERVICE_URL` (+ `OCR_SERVICE_API_KEY`) thì `ScanReceiptCommandHandler` điều phối: `OcrServiceClient` (HttpClient trần, không thêm package) gửi ảnh sang `POST /analyze-bill`, nhận tổng tiền, tên, ngày và chữ ĐÃ lọc; rồi `IAIServiceClient.ClassifyBillAsync` gửi đúng chữ đã lọc đó sang `/bill/classify`. Không cấu hình thì quay về `POST /api/v1/ocr` như trước (đường lui). **Contract `/transactions/scan-receipt` với app không đổi.** Quyết định: (1) AI Service lỗi thì vẫn trả tổng tiền và tên, danh mục để trống — không bắt chụp lại ảnh đã đọc xong; OCR service lỗi thì 503 như cũ. (2) Danh mục người dùng đã chốt cho cửa hàng (bộ nhớ còn ở OCR service, `category_source = memory`) được chuyển sang AI qua `remembered_category` để thắng mọi luật, cho tới khi (d) chuyển bộ nhớ sang AI. (3) Độ tin cậy tính ở backend (`ReceiptConfidence`, cùng công thức cũ, luôn < 0,85, ≤ 0,50 khi OCR service nghi ngờ). (4) OCR service giờ tự bóc ngày giao dịch (`dates.py`, giờ Việt Nam): trước đây `transactedAt` hầu như luôn null vì OCR dính chữ ("Ngay11-04-202613:16"), nay Highlands ra `2026-04-11T13:16+07:00`. Đo thật trên `docker compose`: quét Highlands qua backend → `/api/v1/ocr` của ai-service +0 lượt, `/bill/classify` +1 lượt. **Chưa làm (chờ (d)): dạy lại bộ nhớ cửa hàng khi người dùng sửa danh mục** — trước đây ocr-service tự cập nhật, nay chưa ai gọi `/merchant-category`, và đổi 11 slug về 9 nhóm là mất thông tin (food → dining hay groceries?), nên bộ nhớ hiện không học thêm.*
- [x] **(c) Bảng `receipt_scans` trong `finmate_main`** — *Migration `AddReceiptScans` (chỉ thêm bảng, không đụng bảng nào khác). Mỗi lần quét lưu KẾT QUẢ BÓC TÁCH: `ocr_result` (success/no_amount/unreadable), `amount_cents`, `merchant_name`, `transacted_at`, `category_slug` (null khi AI không nhận ra, không ghi "other" cho có), `category_source` (memory/brand/keyword/product; null ở đường cũ), `confidence`, `needs_review`, `created_at`. **Không lưu ảnh, không lưu chữ OCR kể cả bản đã lọc** (có test chặn việc thêm cột chữ vô tình, và test đọc `information_schema` để chắc bảng chỉ có đúng các cột trên). Quyết định theo yêu cầu: chữ đã lọc chưa lưu; muốn lưu để gỡ lỗi là một quyết định riêng về dữ liệu cá nhân. FK tới `users` `ON DELETE CASCADE`: `DataDeletionJob` hard-delete người dùng thì các lần quét đi theo (có test). Bản ghi ra đời ngay khi quét, TRƯỚC khi người dùng bấm Lưu, nên không động tới số dư, ngân sách hay gamification; cả đường mới (OCR service) lẫn đường cũ (`/api/v1/ocr`) đều ghi. `ScannedReceiptDto` thêm `scanId` ở cuối (client cũ bỏ qua). CHECK ở DB: `ocr_result` chỉ nhận ba giá trị, `amount_cents > 0`. Đo thật trên `docker compose`: backend tự migrate lúc khởi động, quét Highlands ra một dòng đúng. **Chưa làm:** nối bản ghi với giao dịch khi người dùng bấm Lưu (cần cột `transaction_id` và trường tùy chọn `receiptScanId` ở `POST /transactions`, đụng đường ghi tiền, nên để riêng); chưa có job dọn bản ghi cũ.*
- [x] **(d) Kiến thức + dữ liệu huấn luyện vào `finmate_ai`, bỏ DB riêng của ocr-service** — *Migration alembic `0006` (CHỈ thêm, không sửa bảng nào có sẵn) tạo 10 bảng `bill_*` + 2 view, có ORM model (`app/db/models/bill.py`) nên `alembic check` sạch (đã chạy từ DB trống 0001→0006, hạ xuống 0005 rồi lên lại, vẫn sạch). Không dùng lại `raw_samples`/`labeled_samples`: hai bảng đó có CHECK riêng (11 slug BE, nguồn và người gắn nhãn cố định), còn bill dùng 9 nhóm (giữ phân biệt Ăn uống/Siêu thị). Đã nạp toàn bộ dữ liệu cũ bằng `pg_dump -a` + đổi tên bảng: 741 bill có nhãn (47 người dùng chốt, 16 bộ chuẩn, 18 nhập từ file cũ, 660 giả lập), 61 dòng bộ nhớ cửa hàng, 396 thương hiệu, 492 từ khóa, 4 bill lạ, và lịch sử huấn luyện (5 job, 4 model, 4 lần đánh giá, 36 số đo theo nhóm); số dòng khớp từng bảng, các bảng cũ của BE (6 dòng thử) không đổi. `ai-service` giờ tự nạp thương hiệu/từ khóa từ DB (thay bảng trong code, hot reload 60 giây, DB lỗi thì giữ bản đang dùng) và tự tra bộ nhớ cửa hàng theo `user_id_hash` trong `/bill/classify` (thắng mọi luật, mỗi người một bộ nhớ). Backend không còn gửi gợi ý `remembered_category`. `ocr-service` dùng chung `finmate_ai` (đổi tên bảng trong code, bỏ thư mục migration SQL và bộ chạy migration của `db_setup`), `postgres-ocr` đã bỏ khỏi compose. 278 test ai-service (gồm Postgres thật), 394 test ocr-service. Chạy thật: người dùng đã chốt "Highlands" là giải trí thì ra `memory`, người dùng khác vẫn ra brand; quét Coco Mart qua cả chuỗi ra 1.305.000đ, ngày 06/10/2026. **Chưa chuyển:** bảng chat cũ (`chat_expenses`, `chat_unclear_messages`: chat nay lưu ở BE); script huấn luyện model phân loại vẫn nằm ở ocr-service (model chưa bật, 77% < ngưỡng 80%); chưa có đường dạy lại bộ nhớ khi người dùng sửa danh mục (BE chỉ biết 11 slug, đổi về 9 nhóm là mất thông tin; cần quyết định riêng). Bản sao lưu dump và volume `postgres-ocr-data` cũ vẫn còn, chưa xóa.*

## Phase 22 — Dạy lại bộ nhớ cửa hàng khi người dùng chọn danh mục

> Sau Phase 21, bộ nhớ "cửa hàng X người dùng đã chốt là danh mục Y" không học thêm: trước đây ocr-service tự cập nhật, nay backend điều phối và không ai gọi nó.
> Khó ở chỗ backend chỉ biết 11 slug, còn bộ nhớ cũ lưu 9 nhóm của bộ luật bill (đổi ngược `food` thì mất thông tin: Ăn uống hay Siêu thị?).

- [x] **Bảng `bill_user_merchant_choice` (migration `0007`, chỉ thêm một bảng)** — *Lưu ĐÚNG slug backend người dùng đã chọn (CHECK: 10 slug chi tiêu, không `income`), một dòng cho mỗi (người dùng, cửa hàng). Người dùng chỉ chọn được trong danh mục của backend nên nhớ nguyên slug là không mất gì. Bảng cũ `bill_user_merchant_category` (9 nhóm, 61 dòng) giữ nguyên và vẫn được tra SAU bảng mới. `alembic check` sạch; đã chạy 0001→0007, hạ 0007→0006 rồi lên lại trên DB trống.*
- [x] **`POST /api/v1/bill/learn` (ai-service) và `/bill/classify` tra bộ nhớ** — *`learn` nhận hash người dùng, tên cửa hàng, slug (422 nếu không phải slug chi tiêu hợp lệ) và ghi/ghi đè một dòng. `classify` tra bảng mới trước: trúng thì trả đúng slug đó (`source = memory`, `local_category = null`), không đổi qua 9 nhóm. Mỗi người một bộ nhớ. Không ghi tên cửa hàng hay danh mục vào log. Route mới trong contract Backend ↔ AI Service: cần duyệt rồi ghi vào `ARCHITECTURE.md` §3.3.*
- [x] **Backend dạy AI khi lưu hoặc sửa** — *`CreateManualTransactionCommandHandler` (sau khi đã lưu) và `UpdateTransactionCommandHandler` (khi đổi danh mục) gọi `IAIServiceClient.LearnMerchantAsync`. Chỉ khi nguồn là hóa đơn (`Source = Receipt`), có tên cửa hàng, và danh mục là danh mục HỆ THỐNG (danh mục tự tạo có slug riêng AI không biết; thu nhập không phải hóa đơn) — gom trong `ReceiptMemory.ShouldLearn`. Gửi lại cùng `clientRequestId` không dạy lần hai. Best-effort: tự nuốt lỗi và chờ tối đa 3 giây, nên AI Service chết hay treo không làm hỏng hay làm chậm việc lưu.*
- [x] **Kiểm thử** — *ai-service 290 pass (gồm Postgres thật: ghi đè thay vì thêm dòng, bảng mới thắng bảng cũ, theo từng người dùng, CHECK từ chối `income`/nhóm cũ/slug lạ); backend: 14 test mới cho quy tắc dạy và sửa, 4 test client (băm người dùng, tên trường, nuốt lỗi, bỏ chờ sau 3 giây), 4 test HTTP thật (lưu, replay, thủ công không dạy, sửa danh mục). Chạy thật trên `docker compose`: A quét Highlands ra `food`, lưu giao dịch Receipt chọn `entertainment`, quét lại ra `entertainment`; người dùng B vẫn ra `food`; A sửa sang `health`, quét lại ra `health`.*
- [ ] **Chưa làm:** quên một cửa hàng đã dạy (người dùng đổi ý chỉ sửa được bằng cách chọn lại); chưa giới hạn số cửa hàng nhớ cho mỗi người dùng; nhiều người cùng chọn một danh mục cho cùng một cửa hàng (`bill_merchant_votes`) mới đọc bảng cũ, chưa đọc bảng mới.

## Phase 23 — Nối bản ghi quét hóa đơn với giao dịch đã lưu

> `receipt_scans` (Phase 21 bước c) ghi điều AI đọc và đoán, nhưng không biết người dùng rồi có lưu hay chốt gì. Nối hai bên để đo được AI đoán đúng bao nhiêu và đọc tiền đúng bao nhiêu.

- [x] **Cột `receipt_scans.transaction_id` (migration `LinkReceiptScansToTransactions`, chỉ thêm cột)** — *FK tới `transactions` `ON DELETE SET NULL` (xóa hẳn giao dịch thì chỉ gỡ liên kết, bản ghi bóc tách còn; xóa mềm `DeletedAt` vẫn nối nên biết được người dùng lưu rồi xóa) và chỉ mục duy nhất có điều kiện `uq_receipt_scans_transaction_id`: một lần quét thành tối đa một giao dịch, một giao dịch đến từ tối đa một lần quét, để phép đo không đếm trùng. Không lưu thêm trường nào của giao dịch: so qua JOIN.*
- [x] **`POST /transactions` nhận `receiptScanId` tùy chọn (contract chỉ thêm trường)** — *Chỉ có tác dụng với `source = Receipt`. Nối bằng một câu UPDATE có điều kiện (`user_id` khớp VÀ chưa nối) nên hai request cùng lúc không nối đôi, và mã của người khác không bao giờ nối được. Mã sai, của người khác, đã nối, hoặc nguồn không phải Receipt thì BỎ QUA chứ không làm hỏng việc lưu: đây là siêu dữ liệu, giao dịch là tiền của người dùng. Gửi lại cùng `clientRequestId` giữ nguyên liên kết đầu. Hai lần Lưu cùng một lần quét vẫn tạo hai khoản chi thật, chỉ khoản đầu được nối.*
- [x] **Kiểm thử** — *5 test đơn vị cho handler (nối khi Receipt có mã; không nối khi Manual/Voice/không có mã; repository từ chối thì vẫn lưu; replay không nối lại) + 9 test HTTP thật trên Postgres (nối đúng, JOIN so được "AI đoán" với "người dùng chọn" và số tiền, mã của người khác bị bỏ qua, mã không tồn tại bị bỏ qua, chỉ Receipt được nối, chỉ giao dịch đầu được nối, replay giữ liên kết, DB từ chối nối hai lần cho một giao dịch bằng SQLSTATE 23505, xóa hẳn giao dịch chỉ gỡ liên kết). Chạy thật trên `docker compose`: quét Highlands (AI đoán `food`), lưu với `receiptScanId` chọn `entertainment`, JOIN ra `food` so với `entertainment`, tiền 16.000 khớp 16.000.*
- [x] **Client app (`FinMate_FE`, Flutter) gửi `receiptScanId` khi bấm Lưu** — *`scan-receipt` trả `scanId`; app giữ nó trong bản nháp (`DraftTransaction.scanId`), màn rà soát truyền vào `createTransaction`, body `POST /transactions` mang `receiptScanId`. Chỉ giao dịch từ quét hóa đơn có trường này (chat, voice, nhập tay bỏ hẳn). Mất mạng vẫn nối được vì hàng đợi đồng bộ lưu sẵn body request. Không đổi DB local nên không cần migration. Test: bản nháp mang đúng `scanId`; body có/không có `receiptScanId`; test luồng quét → rà soát → Lưu kiểm BE giả nhận đúng mã, giao dịch nhập tay không có mã. `flutter analyze` sạch, 248 test pass.*
- [x] **Camera quét tự động trong `FinMate_FE`** — *Nút chụp trên Android mở ML Kit Document Scanner (`google_mlkit_document_scanner`, tự dò viền, chụp, cắt, nắn thẳng, làm sạch; có nhập ảnh từ thư viện); máy không có Google Play Services hoặc bộ quét lỗi thì quay về camera thường (`image_picker`); iOS dùng camera thường vì plugin chỉ có trên Android. Nút chọn thư viện giữ `image_picker`; OpenCV trên ocr-service vẫn cắt tiếp phía server. APK debug build được; chưa chạy thử trên máy thật.*
- [x] **Bill thật Ngô Gia (BH00037, 2026-10-09): sửa 3 chỗ và thêm vào bộ gold** — *(1) lời chào "NGÔ GIA XIN CHÀO" (trái tim bị đọc thành chữ V) dính vào tên: bỏ từ "xin chào" trở đi khỏi tên cửa hàng, thêm thương hiệu "ngo gia" (code + DB `bill_brands` qua `seed_knowledge_to_pg.py --write`, cần restart ocr-service để nạp). (2) Ngày dạng năm-trước `2026-10-0913:38` của máy POS chưa đọc được: thêm `_YMD` trong `dates.py`. (3) Bộ lọc riêng tư thêm luật che tên người đứng riêng một dòng, chỉ khi nằm ngay sau nhãn liên hệ hoặc ngay trước nhãn địa chỉ (nhãn cho phép OCR sai chữ); kiểm trên 739 bill đã lưu: 0 dòng bị bỏ nhầm. Tên "Vu Thanh Tung" trong 2 bill BH00001 cũ đã che thành [ten] ngay trong DB. Bill mới nhập `bill_labeled_samples` nhãn `gold` (22.000đ, dining). ocr-service 401 test pass.* **Che địa chỉ (cùng ngày):** `privacy.py` bỏ dòng địa chỉ (có dấu phẩy + phường/quận/TP/thành phố, dòng chỉ còn tên thành phố, số nhà + thành phố cuối dòng, dòng số nhà sát một dòng địa chỉ, nhãn "ĐC"/"Địa chỉ" kể cả OCR sai chữ); tên địa danh trong tên cửa hàng/món ("Bệnh viện quận 7", "Vé máy bay Hà Nội - TP.HCM", "Điện lực Hồ Chí Minh") được giữ. Áp lại lên 742 bill đã lưu trong `bill_raw_samples`: 215 bill đổi, 232 dòng bị bỏ (bản sao lưu trước khi ghi: scratchpad, `bill_raw_samples_backup.sql`). ocr-service 404 test pass.
- [ ] **Chưa làm:** chưa có màn hình hay endpoint báo cáo độ chính xác, hiện đo bằng SQL JOIN; chưa có job dọn bản ghi quét cũ chưa nối; chưa thử bộ quét ML Kit và `receiptScanId` trên máy Android thật với BE thật.

## Phase 24 — Tách hóa đơn theo từng món thành nhiều giao dịch

> Một bill có hàng thuộc nhiều nhóm (ví dụ bột giặt và đồ ăn) được tách thành nhiều nhóm kèm số tiền từng nhóm; bấm Lưu thì mỗi nhóm thành một giao dịch, cùng nối về một lần quét. Thiết kế: `docs/superpowers/specs/2026-10-09-receipt-item-split-design.md`; kế hoạch: `docs/superpowers/plans/2026-10-09-receipt-item-split.md`; nhánh `feat/receipt-item-split` (chưa merge). Tiền chỉ có một nguồn là giá OCR đọc tại chỗ; Gemini (khi có) chỉ nhận tên món đã lọc. Công tắc `Receipt:SplitByItemsEnabled` mặc định **tắt**.

- [x] **ocr-service: bóc tách từng món** — *`ocr.read_boxes` trả hộp tọa độ (`read_lines` giữ chữ ký), `ocr._deskew` chỉnh nghiêng trang theo độ dốc quad PaddleOCR, `items.py` ghép tên với giá theo vị trí (tiêu đề cột, tên xuống dòng, phụ kiện "+", giảm giá, phụ thu thuế/phí sau dòng cộng tạm) và chỉ trả `items_ok = true` khi tổng các món + phụ thu - giảm giá khớp **đúng từng đồng** tổng bill và có ≥ 2 món thật. `/analyze-bill` thêm `items[{name, amount, parent, is_discount}]` và `items_ok` (tên món qua `sanitize_lines`). **Đo trên 24 bill chuẩn nhiều dòng, TRONG MẪU (luật chỉnh khi nhìn chính các bill này): `items_ok` 14/24 = 58%; món nhãn tay đúng giá + tên gần đúng 56/58 = 97%; chỉ so tiền 58/58.** Chưa có bộ bill để riêng (held-out).*
- [x] **ai-service: `POST /api/v1/bill/classify-items` + alembic `0008`** — *Thứ tự mỗi món: thừa kế món chính (`inherit`) → luật `bill_keywords` (`rule`) → nhóm mặc định CHỈ của thương hiệu (`merchant`) → Gemini, chỉ khi `allow_ai` và có `GEMINI_API_KEY` (`ai`) → `unknown`. Nhận tên + parent, không nhận tiền. Gemini: một lần mỗi bill, tối đa 20 món, 60 ký tự mỗi tên, schema ép kiểu, sai là bỏ cả kết quả AI; hạn 8 giây và khóa mạch 30 giây; cache theo tên. `0008` thêm `bill_ai_instructions` (một dòng đang dùng, sửa không cần build) và chuyển `bot giat`, `nuoc rua chen`, `giay ve sinh`, `nuoc xa vai` từ `groceries` sang `shopping` (code + DB). Log chỉ ghi số đếm (`bill_items_classified`), không ghi tên món.*
- [x] **BE: tách nhóm, lưu món, nối nhiều giao dịch** — *`ScanReceiptCommandHandler` (khi bật công tắc, `items_ok` và ≥ 2 món chính) gọi `classify-items` với `allow_ai` = cờ đồng ý của người dùng đọc từ DB, rồi `ReceiptSplitter` tự cộng tiền (giảm giá chung chia theo tỷ lệ, phụ thu gộp vào nhóm lớn nhất, `unknown` thành nhóm `categorySlug = null`, tổng lệch thì về đường cũ). `ScannedReceiptDto.groups` (chỉ thêm trường). Bảng `receipt_scan_items` (chỉ trường đã bóc tách, không chữ OCR) và `receipt_scan_links` (thay cột `receipt_scans.transaction_id`, migration chép liên kết cũ; duy nhất mỗi (lần quét, nhóm)). `POST /transactions` thêm `receiptGroup` (làm sạch: bỏ ký tự điều khiển, ≤ 100 ký tự, sai thì lấy slug danh mục); lỗi khi nối sau khi đã lưu tiền không làm hỏng request. `users.share_receipt_items_with_ai` (mặc định tắt, đổi qua `PATCH /users/me`). Bill đã tách ≥ 2 nhóm không dạy bộ nhớ cửa hàng, kể cả khi sửa danh mục bằng `PATCH /transactions/{id}`.*
- [x] **App (`FinMate_FE`): hiện nhóm, lưu nhiều giao dịch** — *Màn rà soát hiện các nhóm (tên, tiền, món), đổi/bỏ nhóm, mỗi nhóm một `POST /transactions` với `receiptScanId`, `receiptGroup` và `clientRequestId` cố định theo (lần quét, nhóm) nên mở lại và lưu lại không tạo khoản trùng; không thoát được giữa lúc đang lưu; công tắc đồng ý chia sẻ tên món với AI ở hồ sơ.*
- [x] **Chạy thật trên `docker compose` (2026-10-10)** — *Build lại backend/ocr-service/ai-service; backend tự áp `AddReceiptScanItemsAndLinks` + `AddUserShareReceiptItemsWithAi` (cột `receipt_scans.transaction_id` đã bỏ); ai-service tự lên alembic `0008` (một dòng `bill_ai_instructions` đang dùng, 4 từ khóa đã sang `shopping`). `seed_knowledge_to_pg.py`: DB không thiếu dòng nào nên không ghi. Bật `Receipt__SplitByItemsEnabled=true`, người dùng thử riêng. Bill giả hai nhóm (bột giặt 90.000 + sữa tươi 30.000 + bánh mì 20.000): `items_ok`, `groups` = shopping 90.000 + food 50.000 = 140.000, `receipt_scan_items` 3 dòng nguồn `rule`. Đồng ý BẬT: cùng kết quả, ai-service ghi `ai_used=false` (không có khóa). Lưu hai nhóm: 2 giao dịch, 2 liên kết đúng nhóm; lưu lại nhóm 1 với `clientRequestId` mới ra 201 (khoản thứ hai) và vẫn một liên kết; gửi lại `clientRequestId` cũ ra 200 cùng giao dịch; `receiptGroup` có ký tự NUL hoặc dài 300 ký tự ra 201, liên kết rơi về slug danh mục, không có 500. Sửa danh mục giao dịch của bill tách: bộ nhớ cửa hàng không học; lưu bill một nhóm (Pizza Hut, `groups = null`) với danh mục khác: có học. JOIN `receipt_scan_items`/`receipt_scan_links`/`transactions`/`categories` so được nhóm AI với danh mục chốt và tiền. Gọi thẳng `classify-items` với `allow_ai=true`: 200, `ai_used=false`, món lạ `unknown`. Test: BE 777 pass; ocr-service 439 pass; ai-service unit 283 pass, cả bộ có Postgres thật 341 pass (một lần chạy song song với test BE có 1 lỗi TimeoutError, chạy lại sạch); FE `flutter analyze` sạch, 264 pass 1 skip. Dữ liệu thử đã xóa, công tắc đã tắt lại.*
- [x] **Hai lỗi thấy khi chạy thật trên bill thật — đã sửa (2026-10-10)** — *(1) Khi nhóm lớn nhất là `unknown`, `categorySlug` cấp trên của `ScannedReceiptDto` thành `null` (`groups[0].CategorySlug`) trong khi đường cũ ra được danh mục: bill nhà thuốc 28b8 tắt công tắc ra `health`, bật ra `null` — app cũ mất danh mục. (2) Bill một danh mục bị tách thành "danh mục + unknown" mỗi khi luật sót một món (28b8: thuốc nhỏ mắt; Chả cá f7f2: "Lòng cá"), tức một bill ra hai giao dịch — sẽ thường gặp khi không có Gemini. Sửa: `ReceiptSplitter.Split` trả `null` (về đường cũ) trừ khi có **ít nhất 2 nhóm có danh mục**; món lạ vẫn thành nhóm `null` riêng (cần xem lại) khi đã có ≥ 2 nhóm biết danh mục; mọi luật tiền giữ nguyên (tổng khớp từng đồng, nhóm > 0, giảm giá/phụ thu, thứ tự). `categorySlug` cấp trên của bill đã tách = nhóm lớn nhất CÓ danh mục (không bao giờ `null`); `amountCents` vẫn là tổng bill. Bill một danh mục giờ ra đúng y như khi tắt công tắc (danh mục từ `/bill/classify`, không `groups`, một dòng `receipt_scans`, không dòng `receipt_scan_items`). Test: BE 777 → **781 pass** (thêm `One_known_group_plus_unknown_returns_null`, `Only_unknown_items_returns_null`, `LargestGroupUnknown_TopLevelSlugIsLargestKnownGroup`, `SingleKnownCategoryWithOneUnmatchedItem_FallsBackToOldPathAndMatchesFlagOff`, test tích hợp `OneKnownCategoryPlusUnknown_ReturnsNoGroupsAndTheOldCategory`; bỏ `UnknownGroupLargest_TopLevelCategoryIsNull` vì khẳng định đúng hành vi lỗi; hai test món lạ đổi sang bill có 2 nhóm biết danh mục). Chạy lại trên stack thật (bật công tắc tạm, người dùng thử riêng): 28b8 → `health`, f7f2 → `food` (Chả Cá Phan), **không `groups`**, response trùng từng trường với lúc tắt công tắc, một dòng quét, 0 dòng món (ai-service vẫn được gọi: `items 2 unknown 1`, `items 4 unknown 1`); bill giả hai nhóm → `groups` shopping 90.000 + food 50.000 = 140.000, cấp trên `shopping`, 3 dòng món. Dữ liệu thử đã xóa (kể cả kiểm tra AI DB theo hash: 0 dòng), công tắc đã tắt lại.* Chưa làm: bộ nhớ/thương hiệu cửa hàng vẫn không được dùng cho món lạ khi tách.
- [x] **Đợt sửa cuối sau review toàn nhánh (2026-10-10)** — *(1) **Tên thuốc không ra Gemini nữa ở nhà thuốc lạ:** trước đây chỉ món khớp từ khóa `healthcare` bị giữ lại, nên nhà thuốc không có trong bảng thương hiệu ("Nhà thuốc Minh Châu") gửi "Panadol Extra", "Efferalgan 500mg" cho Gemini khi người dùng đã đồng ý. Giờ `item_classifier` bỏ qua cả bước Gemini khi phân loại cả bill (tên cửa hàng + mọi tên món) ra `healthcare` hoặc có món nào đã ra `health`; món lạ còn lại thành `unknown`. Lời đồng ý trong app và spec bỏ chữ "không bao giờ", nói đúng: chỉ gửi tên món (không giá, không tên cửa hàng, không thông tin người dùng), hóa đơn nhận ra là thuốc/nhà thuốc thì không gửi. (2) **Từ vựng phụ thu khớp hai bên:** BE `ReceiptSplitter.IsSurcharge` thêm `service` (khớp cả khi dính chữ, như `_SURCHARGE_RE` của ocr-service) và loại dòng "chưa/trước thuế"; "Service charge 5%" giờ gộp vào nhóm lớn nhất thay vì thành nhóm "Chưa phân loại" riêng; hai file ghi chú trỏ nhau, test liệt kê từng từ của ocr-service. (3) **Một lần quét không tách chỉ nối một giao dịch:** khóa nối của lần quét không tách cố định = `category_slug` của lần quét (`unknown` khi null), bỏ qua `receiptGroup` và danh mục giao dịch; lưu hai lần với hai danh mục khác nhau giờ chỉ một dòng nối, JOIN đo độ chính xác không đếm đôi. (4) **`_deskew` có chốt an toàn:** chỉ dùng độ dốc khi có ≥ 3 quad rộng và |độ dốc| ≤ 0,2. (5) Lần quét ghi nguồn `items` được coi là đã tách kể cả khi ghi `receipt_scan_items` lỗi (không dạy nhầm bộ nhớ cửa hàng). (6) `classify-items` nhận cùng tên cửa hàng như `/bill/classify` (tên OCR thô). (7) Cụm giống số tiền (`45,000`, `12.000đ`) bị bỏ khỏi tên trước khi gửi Gemini; tên rỗng sau khi bỏ thì không gửi. (8) Log lỗi Gemini ghi mã lý do (`http_500`, `bad_count`, `overall_timeout`), vẫn không ghi tên món. (9) App: danh sách giao dịch không hiện hai dòng khi lưu lại cùng một giao dịch (200). (10) Spec/rules draft/plan/chú thích code khớp lại với code. **Đo lại sau (4), cùng lệnh bước 4b:** `items_ok` 14/24 = 58% (trước 14/24), món đúng giá + tên gần đúng 56/58 = 97% (trước 56/58) — không đổi, vẫn trong mẫu. Test: BE 781 → **799 pass**; ocr-service 439 → **441 pass**; ai-service unit 283 → **289 pass**, cả bộ có Postgres thật 341 → **347 pass**; FE `flutter analyze` sạch, 264 → **265 pass**, 1 skip.*
- [x] **Cập nhật `ARCHITECTURE.md` và `TECH_STACK.md` (Task 17, đã được duyệt 2026-10-11)** — *ARCHITECTURE: ghi chú ở §1, cây thư mục ai-service (§3.1), hợp đồng `classify-items` + các trường mới của `analyze-bill`/`scan-receipt`/`transactions`/`users/me` (§3.3), mục mới §3.4 (luồng tách hóa đơn), bảng `receipt_scans`/`receipt_scan_items`/`receipt_scan_links` + cột `users.share_receipt_items_with_ai` (§4.1, sửa luôn con số "20 bảng" đếm sai thành 25), `bill_ai_instructions` (§4.2), quyền riêng tư của Gemini (§7.3). TECH_STACK: `ocr-service` + PaddleOCR trong image riêng (ai-service vẫn không có paddleocr/torch), Gemini là bên thứ ba duy nhất (tùy chọn, mặc định tắt), các dòng `ocr-service` và `rasa` trong danh sách container, biến môi trường `OCR_SERVICE_*`, `Receipt__SplitByItemsEnabled`, `GEMINI_*`. Chưa ghi (vẫn lỗi thời): các route `/chat/parse`, `/bill/classify`, `/bill/learn`, `/transactions/chat`, các bảng `bill_*` khác và Rasa ở ARCHITECTURE; việc gỡ Tesseract chưa quyết định.*
- [x] **Tài liệu lượt hai (2026-10-11): `ARCHITECTURE.md` và `TECH_STACK.md` khớp mã cho phase 19–23** — *Thêm: các route `/chat/parse`, `/bill/classify`, `/bill/learn`, `/transactions/chat`; Rasa (§3.6); `ocr-service` (§3.5); điều phối của backend (§2.4); hai bộ danh mục và bảng ánh xạ (§3.7); danh sách đúng các bảng AI (24 bảng không tính `alembic_version` + 2 view) và mục "Giới hạn đã biết". Sửa tại chỗ: client là Flutter, Tesseract là lựa chọn cấu hình chứ KHÔNG phải dự phòng lúc chạy (ocr-service lỗi thì quét báo lỗi), thứ tự `/bill/classify`, `OCR_SERVICE_API_KEY` để trống không dừng khi khởi động mà gây 401. Một reviewer đã đối chiếu 54 khẳng định với mã. Chưa sửa vì ngoài hai file: `AGENTS.md` (Kotlin), `CLAUDE.md` (12 bảng AI), comment trong `ocr-service/.env.example` và `app/core/config.py`.*
- [x] **Sửa lỗi tổng tiền trên hóa đơn Ministop thật (2026-10-11)** — *`total_amount` ra 61.000 (số "Tạm tính") thay vì 47.000 ("Tổng cộng (gồm thuế)"). Nguyên nhân: tên món "TraYuzumatongboncha" (mật ong) chứa chuỗi "tong" nên bị coi là nhãn tổng và kéo số dòng kế tiếp (61.000); "Tong cong" thì lấy nhầm 50.000 (tiền khách đưa, OCR đọc số bên phải TRƯỚC nhãn bên trái). Sửa trong `extract._keyword_total`: "tong" phải bắt đầu một từ (cho phép MỘT ký tự nhiễu dính trước), và số sau nhãn chỉ nhường cho nhãn kế tiếp khi dòng tiếp theo là nhãn tiền mặt/tiền thừa. Reviewer chặn bản đầu (luật phụ quá rộng làm nhãn tổng sai trên 5/30 bill thật, bị cơ chế số lặp che), bản cuối: 30/30 bill giữ nguyên tổng, nhãn tổng, nguồn và điểm nhận diện; ocr-service 448 test. Chạy lại trên stack thật: Ministop = 47.000. Còn hở: khối xen kẽ nhãn-số có dòng "Tiền thừa" vẫn có thể ra giá món (cả mã cũ lẫn mới), cách sửa gốc là ghép nhãn với số theo tọa độ dọc; giờ in (12:31:13) chưa gắn vào ngày vì OCR tách hai dòng.*
- [ ] **Chưa làm / chưa kiểm chứng:** (1) **Đường Gemini chưa chạy lần nào** (không có `GEMINI_API_KEY` bản trả phí): schema mảng, header, xử lý 429/503 chỉ có test giả; chưa đọc lại điều khoản dữ liệu. (2) **Chưa có bộ bill held-out**; số 58%/97% là trong mẫu. (3) 10/24 bill chuẩn vẫn không tách được món (không có tiêu đề cột, ảnh mờ, cột giá giữa bảng, giá lệch hàng). (4) AEON "KHUYENMAI@" bị `privacy.py` che như email. (5) Màn hình FE chưa qua bước thiết kế (`ui-ux-pro-max`, `taste-skill` không có trong phiên). (6) Quét lại cùng một bill (lần quét mới) rồi lưu vẫn tạo khoản chi trùng. (7) Độ trễ quét (thêm một lượt gọi ai-service, tới 8 giây khi có Gemini) chưa đo p95. (8) Nhóm mặc định của thương hiệu có thể cho một lần tách tự tin nhưng sai ở siêu thị có thương hiệu (món lạ nhận nhóm của cửa hàng). (9) Nhóm "Chưa phân loại" lưu thành giao dịch không có danh mục. (10) Hóa đơn thuốc chỉ được nhận ra bằng luật/từ khóa: thuốc trên bill không nhận ra là thuốc vẫn có thể bị gửi tên khi đã đồng ý. (11) Việc nhỏ còn để lại: ocr-service không gắn `parent` cho giảm giá ngay dưới món nên chỉ có giảm giá chia chung; slug nhóm phân biệt hoa thường trong chỉ mục duy nhất. (12) Bill Ngô Gia không chạy lại được trên stack thật (không tìm thấy ảnh), thay bằng 28b8 và Pizza Hut. (13) `ARCHITECTURE.md`/`TECH_STACK.md` (route, bảng, trường hợp đồng mới; Gemini là bên thứ ba mới) chờ duyệt (Task 17).

## Phase 25 — Bám luồng chat và quét bill theo `finance_ai_pipeline_prompt.md` (2026-10-12)

> Kế hoạch: `docs/superpowers/plans/2026-10-12-chat-ocr-flow-alignment.md`. Voice/ASR nằm ngoài (người khác làm, sẽ tích hợp qua cùng đường văn bản). Nhánh `feat/chat-ocr-alignment`.

- [x] **Bộ kiểm thử cố định + báo cáo đo** — *`ai-service/tests/fixtures/eval/{chat_cases,item_cases}.jsonl` (63 câu chat, 72 món), `scripts/eval_flows.py`, kết quả trong `docs/evaluation.md`. Nhãn viết trước khi sửa luật. Chat 50/63 → 62/63; món 51/72 → 52/72 (chỉ đo lớp luật, Gemini giả luôn "unknown").*
- [x] **Gemini đứng trước nhóm mặc định cửa hàng** — *Sửa đúng lỗi Ministop: món lạ (bình nước) hỏi Gemini trước, nhóm mặc định chỉ còn là đường lui. Kiểm tra trên stack thật.*
- [x] **Bảng nhớ tên món `bill_item_cache` (alembic `0009`)** — *Câu trả lời hợp lệ của Gemini được lưu, lần sau `source = "cache"` không gọi lại. Lọc tên rác (mã, quá ngắn/dài, ký tự điều khiển); câu đầu tiên thắng. Dùng chung, không gắn người dùng.*
- [x] **Khớp gần đúng từ khóa (OCR sai một chữ)** — *Chỉ cặp chữ hay nhầm hoặc thiếu/thừa một chữ; ngưỡng điểm một mình không tách được "nước muối"/"nước suối" nên không dùng.*
- [x] **Chat: tách nhiều khoản, ngày, câu mơ hồ** — *`clauses.py`, `dates.py`; `entries[]`, `occurred_at`; `education`/`family` trong từ vựng chat; "ăn cơm" không số tiền → `needs_amount` kể cả không có Rasa.*
- [x] **Backend: nhiều bản nháp, ngày, cảnh báo trùng, `needsReview`** — *`drafts`, `occurredAt`, `possibleDuplicateTransactionId` (chat ±10 phút, quét ±3 giờ; chỉ cảnh báo), `ScannedReceiptDto.NeedsReview`. Backend 830 test xanh, ai-service 440.*
- [x] **Chạy thật trên `docker compose`** — *Chat có Rasa: nhiều khoản → 3 bản nháp, "hôm qua" → ghi đúng ngày, khoản trùng → hỏi lại kèm mã, "mua nước" không ép nhóm, "đóng học phí" → education, "chuyển khoản" → chuyển tiền. Quét bill tổng hợp: tách shopping 570.000 + food 100.000.*
- [x] **App (`FinMate_FE`): màn chat (2026-10-12)** — *Mỗi khoản trong câu nhiều khoản là một thẻ nháp riêng (kèm lời báo "N khoản"); thẻ và màn rà soát hiện ngày khi khác hôm nay, cảnh báo "có thể trùng" (cả bill hoặc từng nhóm) và "AI chưa chắc" khi `needsReview`; thẻ "Đã ghi" hiện ngày. `flutter test` 282 xanh, `flutter analyze` sạch; fixture là phản hồi thật của BE. Chưa thử trên máy Android thật.*
- [ ] **Chưa làm:** chuyển ngưỡng/hiệu chỉnh độ chắc; nhớ lựa chọn theo từng người dùng; fixture "mua đồ 1,5tr" (kỳ vọng không nhóm) cố ý để đỏ vì từ vựng "mua đồ" → shopping; eval món chưa đo được tác dụng của Gemini/cache (Gemini giả).

## Phase 26 — Gộp phần phân loại của chat và món trên bill (2026-10-12)

> Kế hoạch: `docs/superpowers/plans/2026-10-12-unify-classification.md`. Nhánh `feat/unify-classification`. Cùng một cụm từ đi bằng chat hay bill phải ra cùng danh mục (prompt `finance_ai_pipeline_prompt.md` §5.3).

- [x] **Bộ kiểm thử nhất quán** — *66 cụm qua cả hai đường, nhãn viết trước (`consistency_cases.jsonl`, `eval_flows.py --consistency`). Baseline: 31/66 cùng danh mục.*
- [x] **`category_resolver`** — *Gộp từ khóa bill + từ vựng chat + từ điển cửa hàng; dài nhất thắng, hòa thì slug cụ thể; khớp gần đúng OCR; không biết thì `None`.*
- [x] **Món trên bill và chat dùng chung** — *`classify_items` và `parse_chat` gọi `resolve`; chat tắt khớp gần đúng; chat tra `bill_item_cache` chỉ để gợi ý. Món 52→60/72, sai-mà-tự-tin 2→0; chat giữ 62/63; nhất quán 65/66.*
- [x] **Ảnh chụp màn hình nhiều đơn (2026-10-12)** — *ocr-service đã tách `orders` từ lâu nhưng backend bỏ qua nên chỉ lấy đơn đầu (Shopee 3 đơn → 1 khoản). Backend giờ trả `orders` + `incompleteOrders`, app hiện một thẻ nháp mỗi đơn. Chạy thật: 78.742 + 224.400 + 247.253, đều `shopping`.*
- [ ] **Chưa làm:** Gemini cho chat (cần xin đồng ý riêng); chuyển `bill_brands`/`bill_keywords` sang slug backend; bỏ bản sao bảng từ khóa trong ocr-service; câu truy vấn tổng ("tiêu cho thuốc bao nhiêu") vẫn dùng từ vựng cũ.

## Backlog (Future — Không trong MVP scope)

- *(Receipt OCR và Voice input đã kéo lên MVP ngày 2026-09-11 — xem Phase 10 quyết định #4)*
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

> **Quy tắc đếm:** đếm **mọi** checkbox trong phase, kể cả sub-task lồng — không chỉ task cấp 1.
> Backlog không tính vào tổng (ngoài MVP scope). Đếm lại bằng:
> `grep -cE '^\s*- \[x\]' .context/TASKS.md`

| Phase | Status | Tasks Done / Total |
|---|---|---|
| Phase 0 — Setup | `[x]` | 20 / 20 |
| Phase 1 — Auth & Profile | `[x]` | 28 / 28 *(+1: Google login, kéo từ Backlog)* |
| Phase 2 — Financial Accounts | `[x]` | 14 / 14 *(guard has-transactions hoàn thành ở Phase 4, xem note dưới)* |
| Phase 3 — Categories | `[x]` | 9 / 9 |
| Phase 4 — Notifications & Transactions | `[x]` | 44 / 44 *(3 sub-task cascade budget gỡ block ở Phase 5; 4 sub-task EXP/streak/mission + 2 test gỡ block ở Phase 7)* |
| Phase 5 — Budget & Goals | `[x]` | 36 / 37 *(còn 1 sub-task "gửi push notification" của `BudgetAlertJob` `[!]` — chưa có FCM trong TECH_STACK.md, xem note)* |
| Phase 6 — Reports | `[x]` | 18 / 18 |
| Phase 7 — Gamification | `[x]` | 27 / 27 |
| Phase 8 — Admin | `[x]` | 14 / 14 |
| Phase 9 — AI Service | `[x]` | 27 / 28 *(1 task `[-]` Skipped: 3 bảng `ab_*`, A/B testing ngoài MVP scope)* |
| Phase 10 — Đối chiếu Core User Flows | `[x]` | 21 / 22 *(#1, #2, #4 xong; 1 task `[-]` Skipped: OTP)* |
| Phase 11 — Đối chiếu lại Flow 1 & 2 | `[x]` | 7 / 7 |
| Phase 12 — Push thật qua FCM | `[x]` | 9 / 9 |
| Phase 13 — Quản trị người dùng | `[x]` | 9 / 9 |
| Phase 14 — Đồng bộ offline & rate limit | `[x]` | 9 / 9 |
| Phase 15 — Nhánh 85% của Flow 1 | `[x]` | 8 / 8 |
| Phase 16 — Sẵn sàng production | `[x]` | 8 / 8 |
| Phase 17 — OTP qua email | `[x]` | 9 / 9 |
| Phase 18 — Google login qua trình duyệt | `[x]` | 8 / 8 |
| Phase 19 — OCR qua ocr-service | `[ ]` | 7 / 8 *(chờ duyệt gỡ Tesseract)* |
| Phase 20 — Chat qua Rasa | `[ ]` | 7 / 8 *(contract `/chat/parse` + Rasa đã ghi vào ARCHITECTURE/TECH_STACK với sự đồng ý của chủ dự án 2026-10-11; còn: chưa chuyển kho tin nhắn chưa rõ)* |
| Phase 21 — Tách phân loại hóa đơn sang AI Service | `[x]` | 4 / 4 |
| Phase 22 — Dạy lại bộ nhớ cửa hàng | `[ ]` | 4 / 5 *(chưa có cách quên một cửa hàng đã dạy, giới hạn số cửa hàng, phiếu bầu từ bảng mới)* |
| Phase 23 — Nối bản ghi quét với giao dịch | `[ ]` | 5 / 6 *(còn báo cáo độ chính xác, dọn bản ghi cũ, thử trên máy Android thật)* |
| **Total** | | **326 / 328** |

**Còn lại:** 0 task chưa làm trong MVP scope, 0 task `[!]` chờ duyệt. Còn 2 task `[-]` bỏ có
chủ ý (OTP, 3 bảng `ab_*`).

**Vẫn lệch docx, đã biết, chưa làm:** nhánh confidence 85% (Flow 1 bước 5.2/5.3 — AI đã trả
confidence đầy đủ nhưng backend chưa dùng nó để chọn giữa push một chạm và hộp thoại chọn
danh mục; **hết bị FCM chặn từ Phase 12**, giờ chỉ còn thiếu phần `data` payload và contract
với client); `ForecastJob` (ARCHITECTURE.md §5 ghi
06:00, docx Flow 3 mục 4 ghi 22:00, hiện **không có job nào** — forecast chỉ tính khi client
gọi); trạng thái cảm xúc Mascot xanh/vàng/đỏ (Flow 3 mục 1, chưa có contract).

**Chưa đối chiếu docx lần nào:** Core Flow 3 và Core Flow 4. Phase 11 chỉ đọc trọn vẹn Flow 1
và Flow 2; ba điểm Flow 3 ghi ở trên là nhặt được TÌNH CỜ khi sửa Flow 1–2, không phải rà
soát. Con số 275/277 không chứng minh được gì cho hai flow đó — đúng loại sai lệch mà Phase
11 đã cho thấy một bảng checkbox không tự phát hiện nổi.

---

**Verify Phase 2 (2026-09-10):** `dotnet build` sạch 0 warning; `dotnet test` xanh 20/20 (chạy qua container SDK 9.0); `dotnet ef migrations has-pending-model-changes` sạch; `docker compose up` full stack từ volume rỗng — migration áp dụng sạch, seed đủ 5 provider configs; smoke test curl end-to-end (register/login → tạo bank account → tạo trùng package_name [409] → tạo cash account → list → toggle monitoring → get balance → delete → get balance sau xóa [404]) đều đúng như thiết kế. Một bug hạ tầng test được tìm thấy và sửa: `AuthApiFactory` set config qua `Environment.SetEnvironmentVariable` (process-wide) — khi 2 test class dùng factory riêng chạy song song (xunit mặc định chạy khác class song song), race trên biến môi trường khiến 2 `WebApplicationFactory` đôi khi trỏ cùng lúc vào 1 Postgres container, gây deadlock/connection-refused ngẫu nhiên. Fix bằng cách gom mọi integration test class vào chung 1 `[Collection("Integration")]` để chạy tuần tự.

**Verify Phase 3+4 (2026-09-10):** `dotnet build` sạch 0 warning; `dotnet test` xanh 94/94, chạy lặp lại 2 lần liên tiếp không flake (chạy qua container SDK 9.0); `dotnet ef migrations has-pending-model-changes` sạch; `docker compose up` full stack (bao gồm `ai-service` thật — vẫn chỉ là FastAPI scaffold trống của Phase 0, chưa có route `/api/v1/analyze`) — smoke test curl end-to-end: seed đủ 11 category; tạo cash account → tạo manual transaction (debit) → balance giảm đúng → xóa account khi còn giao dịch bị 409 → xóa transaction → balance khôi phục → xóa account thành công; gọi `/notifications/analyze` với package đã whitelist nhắm vào `ai-service` thật (chưa có route) → xác nhận trả đúng `503 NOTIFICATION_AI_SERVICE_UNAVAILABLE` (không crash 500), `notification_logs.status='failed'` đúng trong DB, log không có exception chưa xử lý. Nhánh AI thành công (financial → tạo transaction draft → confirm) được cover đầy đủ qua integration test với `FakeAIServiceClient` (chưa thể verify qua curl thật vì Phase 9 chưa code AI Service) — cùng giới hạn đã ghi nhận ở Google login.

**Verify Phase 5 (2026-09-11):** `dotnet build` sạch 0 warning; `dotnet test` xanh 153/153 (chạy qua container SDK 9.0); `dotnet ef migrations has-pending-model-changes` sạch; `docker compose up --build` full stack từ volume rỗng — migration áp dụng sạch, 4 recurring job đăng ký đủ (`budget-alerts`, `goal-deadline-check`, `data-deletion`, `retry-failed-notifications`), log không có exception chưa xử lý (ngoài 1 lỗi `__EFMigrationsHistory` lúc khởi động DB rỗng, EF tự xử lý, đã có từ các phase trước). Smoke test curl end-to-end: seed đủ 11 category → tạo budget food + budget tổng → tạo budget food lần 2 nhận 409 → chi 250k vào food thì cả 2 budget cùng lên 250k → đổi giao dịch sang transport thì food về 0 còn budget tổng giữ 250k → ghi nhận thu nhập 15tr không tiêu hạn mức nào → xóa giao dịch thì mọi budget về 0 → nâng hạn mức food lên 2tr, `%` tính lại đúng → tạo mục tiêu 10tr, góp 3tr (30%, on-track, cần 77.778đ/ngày) → góp nốt 7tr thì status thành `completed` → góp tiếp nhận 422 → **số dư tài khoản không đổi vì đóng góp mục tiêu là bookkeeping thuần** (10tr ban đầu + 15tr thu nhập = 25tr).

Hai lỗi tìm được khi chạy thật (không lộ ra ở unit test) và đã sửa: (1) Npgsql từ chối `DateTimeOffset` offset +07:00 cho cột `timestamptz` khiến mọi endpoint budget trả 500 — mốc chu kỳ giờ trả về ở UTC, xem note biên chu kỳ ở trên; (2) response summary có field `totalSpentCents` = 0 nằm ngay trên dòng budget tổng đang hiện 250k (vì nó chỉ cộng budget theo category) — đã tách hẳn `TotalBudget` khỏi `CategoryBudgets` để không còn con số nào đọc nhầm được.

**Verify Phase 6+7 (2026-09-11):** `dotnet build` sạch 0 warning; `dotnet test` xanh 281/281 (chạy qua container SDK 9.0); `dotnet ef migrations has-pending-model-changes` sạch; `docker compose up --build` full stack từ volume rỗng — migration sạch, seed đủ 7 mission + 8 mascot item, **8 recurring job** đăng ký đủ (`daily-summary`, `insight-generator`, `streak-check`, `mission-reset` + 4 job cũ), log không có exception chưa xử lý (ngoài 1 lỗi `__EFMigrationsHistory` lúc khởi động DB rỗng, EF tự xử lý, đã có từ các phase trước).

Smoke test curl end-to-end: tạo giao dịch trải nhiều ngày → `monthly-summary` khớp `category-breakdown` và `timeline` (900k / 5 giao dịch, phần trăm tổng đúng 100) → `forecast` giữ nguyên phần đã tiêu và báo `confidence: low` khi dữ liệu mỏng → profile khởi tạo lazily, giao dịch thủ công cộng EXP và hoàn thành `daily_manual_1` → đóng góp mục tiêu nhảy 2 bậc lên level 3 và mở khóa 3 mascot item (kèm `celebration` trong response) → mặc mũ OK (200), mặc 2 mũ cùng loại bị chặn (422) → xóa giao dịch trừ lại đúng 10 EXP.

**Một lỗi tìm được khi chạy thật (không lộ ra ở unit test) và đã sửa:** `GetMonthlySummaryQuery` chỉ cộng `daily_summaries` cho các ngày trước hôm nay, nên trên môi trường vừa dựng (và bất kỳ ngày nào `DailySummaryJob` lỗi) báo cáo **âm thầm thiếu dữ liệu** — smoke test cho tổng tháng 300k trong khi breakdown và timeline cùng ra 900k. Không thể suy "thiếu dòng summary" thành "ngày đó không tiêu gì". Đã sửa: đọc summary có sẵn rồi lấp mọi ngày chúng chưa phủ bằng dữ liệu live, khớp theo ngày nên ngày đã chốt không bị cộng hai lần; đổi lại là một truy vấn có giới hạn (tối đa 31 ngày của 1 user, đi qua index `(user_id, transacted_at, id)`).

*Last updated: 2026-09-11*
*Next priority: chốt push provider (FCM) để gỡ task `[!]` cuối cùng, hoặc dùng ngưỡng confidence 85% của docx Flow 1 bước 5.2/5.3 — AI đã trả confidence đầy đủ nhưng backend chưa dùng nó để chọn giữa push xác nhận một chạm và hộp thoại chọn danh mục.*

**Verify Phase 10 #1+#2 (2026-09-11):** `dotnet build` sạch 0 warning; `dotnet test` xanh **302/302** (281 → 302, thêm 21 test; chạy qua container SDK 9.0 + Docker socket cho Testcontainers); `dotnet ef migrations has-pending-model-changes` sạch; `docker compose up --build` từ volume rỗng — 2 migration mới áp sạch, `\d transactions` xác nhận đủ `counter_account_id` + FK Restrict + `chk_transactions_transfer_shape`, `\d budget_periods` đủ 3 cột `alert_70/90/100_sent_at`, 8 recurring job đăng ký đủ, log không có exception chưa xử lý (ngoài lỗi `__EFMigrationsHistory` lúc khởi động DB rỗng đã có từ các phase trước).

Smoke test curl end-to-end luồng transfer: tạo 2 ví (5tr / 0) + budget tổng 3tr → rút ATM 2tr qua `POST /transactions/transfer` → **số dư 3tr / 2tr, tổng tài sản vẫn 5tr** → `budgets` báo `spentCents = 0`, `monthly-summary` báo `totalSpent = 0` và `totalIncome = 0`, `forecast` báo `spentSoFar = 0` (đúng mục tiêu của quyết định #1: rút tiền không phải tiêu tiền) → chuyển vào chính ví đó nhận 422 `TRANSACTION_TRANSFER_SAME_ACCOUNT` → tạo transfer qua `POST /transactions` thường nhận 400 kèm thông điệp chỉ sang đúng endpoint → xóa ví đang là ĐÍCH của transfer nhận 409 → sửa transfer 2tr thành 3.5tr thì số dư thành 1.5tr / 3.5tr (tổng vẫn 5tr) → đổi sang `Debit` nhận 422 `TRANSACTION_TYPE_CHANGE_NOT_ALLOWED` → xóa transfer thì 2 ví về đúng nguyên trạng 5tr / 0.

**Một bug có sẵn được tìm thấy và sửa:** `DeleteTransactionCommandHandler` hoàn `TransactionExpRewards.ConfirmTransaction` (10 EXP) cho MỌI giao dịch, trong khi giao dịch tự nhập chỉ được cộng 5 lúc tạo — xóa một giao dịch thủ công ăn mất 5 EXP user chưa từng có. Nay tra theo `Transaction.Source`. Integration test `DeletingAConfirmedTransaction_TakesBackTheExpItGave` đã khoá cứng đúng con bug này (assert `-10` trên một giao dịch thủ công) nên vẫn xanh suốt — tên test lại mô tả đúng hành vi đáng lẽ phải có. Đã sửa assertion về `-5` kèm ghi chú.

**Verify Phase 9 (2026-09-11):** `pytest` xanh **104/104** (81 unit + 23 integration; test tích hợp tạo database riêng trên chính server Postgres của compose rồi xoá đi); `black --check`/`isort --check-only`/`ruff check` sạch; `alembic upgrade head` → `downgrade base` → `upgrade head` round-trip sạch, `alembic check` không thấy drift. Phía backend: `dotnet build` sạch 0 warning, `dotnet test` xanh **302/302**, `dotnet ef migrations has-pending-model-changes` sạch.

`docker compose up --build` từ volume rỗng — 12 bảng AI DB lên sạch, 8 pattern seed đủ, 8 recurring job đăng ký đủ, **log AI Service không có `notification_body`/`amount_cents`/số tài khoản nào lọt ra** (grep 0 kết quả), không exception chưa xử lý ở cả hai service.

**Smoke test curl qua BACKEND (không gọi thẳng AI Service):** thông báo MB Bank thật → `POST /notifications/analyze` trả **200 kèm `draftTransactionId`** (trước Phase 9 luôn là `503 NOTIFICATION_AI_SERVICE_UNAVAILABLE`) → giao dịch nháp đúng 75.000đ / `Debit` / merchant `HIGHLANDS COFFEE` / `categoryId` trỏ đúng category hệ thống `food` → thông báo OTP trả `Processed` nhưng **không** tạo nháp → gửi lại đúng thông báo cũ trong 5 phút thì backend dedup theo `content_hash`, không tạo nháp thứ hai → `POST /transactions/parse` với câu "trưa nay ăn phở 45k ở Phở Thìn" trả đủ field prefill (45.000đ, `food`, merchant "Phở Thìn") → confirm rồi `PATCH` đổi danh mục sang `entertainment` thì `user_feedback` có dòng mới **đã liên kết được về `pipeline_requests`** → `scripts/feedback_batch.py` biến nó thành `labeled_samples(category_slug='entertainment', labeled_by='user_feedback', is_gold=true)`. Vòng phản hồi khép kín, verify bằng dữ liệu thật trong `finmate_ai` chứ không bằng suy luận.

**Vòng đời model chạy thật trong container:** `seed_dataset.py` (236 mẫu → 168/38/30) → `train.py --stage classifier` → `evaluate.py` (accuracy 1.000 trên split test) → `promote.py` → AI Service **tự nạp model trong 60 giây không cần restart**, `pipeline_requests.classifier_version` chuyển từ NULL sang `1.0.0` và confidence đổi từ hằng số của luật (0.92) sang xác suất thật của model (0.840/0.821). Categorizer train xong đạt accuracy 0.684 trên split test nên **`promote.py` từ chối** (ngưỡng 0.70) — nhánh từ điển tiếp tục phục vụ. Đây là cổng chặn hoạt động đúng như thiết kế, không phải lỗi: model 111 mẫu / 10 lớp đang tệ hơn baseline từ điển.

> **Cảnh báo về con số:** corpus hiện tại là dữ liệu **tổng hợp** (`scripts/generate_bootstrap_corpus.py`), sinh ra từ chính những template mà Extractor và từ điển đã biết. Accuracy 1.000 của classifier và 99.4% của từ điển **không phải độ chính xác ngoài đời** — chúng chỉ chứng minh đường ống chạy đúng. Con số thật chỉ có sau khi thay bằng mẫu thông báo thật từ 5 provider (user sẽ gửi).

**Một bug thật của backend được tìm thấy qua smoke test và đã sửa:** Npgsql **từ chối** ghi `DateTimeOffset` có offset khác 0 vào cột `timestamptz` — nó ném `ArgumentException` chứ không tự quy đổi. Client Android chạy ở Việt Nam gửi mốc thời gian kèm `+07:00`, nên **mọi** endpoint nhận `DateTimeOffset` từ client (`/notifications/analyze`, `POST|PATCH /transactions`, `/transactions/transfer`, deadline saving goal…) đều trả 500. Lỗi không lộ ra ở test nào vì `WebApplicationFactory` và mọi smoke test trước đều gửi mốc UTC (`Z`). Sửa bằng `UtcDateTimeOffsetConverter` áp ở `FinMateDbContext.ConfigureConventions` cho toàn bộ `DateTimeOffset`/`DateTimeOffset?` — chuẩn hoá ở tầng lưu trữ thay vì rải trong từng handler, vì đây là ràng buộc của tầng lưu trữ và bỏ sót một handler mới sẽ tái hiện đúng lỗi này. Không đổi schema (`has-pending-model-changes` sạch), `dotnet test` vẫn 302/302. Cùng gốc với quyết định "mốc chu kỳ luôn trả về ở offset 0" đã ghi ở Phase 5 — lần này ở đầu bên kia của contract.

**Verify Phase 8 (2026-09-12):** `dotnet build` sạch 0 warning; `dotnet test` xanh **345/345** (306 → 345, thêm 39 test); `dotnet ef migrations has-pending-model-changes` sạch. Phía AI Service: `pytest` 104/104, `black --check`/`isort --check-only`/`ruff check` sạch.

**Smoke test curl end-to-end** trên `docker compose` (admin từ `ADMIN_SEED_EMAIL` + một user thường):

1. **Sáu nhóm endpoint admin đều trả 403 cho user thường và 401 cho ẩn danh** — đây là bài test quan trọng nhất của phase: quên `[Authorize(Policy = AdminOnly)]` một lần thì endpoint rơi về `FallbackPolicy`, bất kỳ ai đã đăng nhập cũng gọi được, mà response vẫn 200 nên không có gì báo.
2. Provider config: tạo → trùng `provider_key` **409** → sửa `display_name` **200** → sửa `provider_key` **422** → tắt **200** → biến khỏi danh sách mặc định, còn nguyên với `?includeInactive=true`.
3. Category hệ thống: tạo → user tạo giao dịch 250k gắn danh mục đó → **tắt** → user không còn thấy danh mục khi tạo giao dịch mới, nhưng **`category-breakdown` vẫn báo đủ 250.000đ kèm đúng tên danh mục, và chi tiết giao dịch vẫn hiện `categoryName`**. Đây chính là điều cột `is_active` sinh ra để bảo vệ; dùng `deleted_at` thì con số đó sẽ biến mất không báo. Sửa slug **422**, tạo trùng slug `food` **409**.
4. Mission: tạo → sửa `condition_target`/`exp_reward` **200** → đổi `condition_type` **422** → đổi `period_type` **422** → tắt **200**.
5. Users: response **không chứa** `amountCents`/`balanceCents`/`passwordHash`/`refreshToken` (grep trên chuỗi thô); khoá user → **refresh token của họ trả 401**; admin tự khoá mình **422** nhưng tự mở khoá **200**.
6. `audit-logs`: đủ vết của cả 4 bước trên (`Admin.ProviderConfig.*`, `Admin.Category.*`, `Admin.Mission.*`, `Admin.User.Locked`) bên cạnh các sự kiện `Auth.*` cũ; lọc theo `eventType` trả đúng, `metadata` mang đủ id và slug.
7. `ai-stats` khi AI Service **đang chạy**: trả cả hai nửa — phía backend `categoryCorrectionRate = 0.111` (1/9 giao dịch bị người dùng đổi danh mục, số liệu thật còn lại từ smoke test Phase 9), phía AI Service `classifier 1.0.0 accuracy 1.0`, `categorizer version: null` (chưa promote, đang chạy bằng luật), 239 raw / 237 labeled / 2 chưa gán nhãn. Khi **`docker compose stop ai-service`**: vẫn **200**, `aiService: null` kèm lý do — dashboard không sập theo AI Service.

**Hai lỗi có sẵn được tìm thấy và sửa trong phase này:**

1. **`tests/conftest.py` của AI Service (Phase 9) bắt MỌI test phụ thuộc Postgres.** Fixture dọn bảng để `autouse=True` ở conftest gốc, nên trên máy không có Postgres thì cả 104 test bị SKIP chứ không chỉ phần tích hợp — trái đúng điều `README.md` và ghi chú Verify Phase 9 đã khẳng định. Lần kiểm tra bản clone sạch ở Phase 9 lọt lưới vì lúc đó `postgres-ai` đang chạy. Đã tách toàn bộ fixture chạm DB sang `tests/integration/conftest.py`; nay không có Postgres thì 81 test đơn vị vẫn chạy, chỉ 23 test tích hợp SKIP.
2. **`audit_logs.metadata` serialize khác cấu hình JSON của chính API.** PascalCase trong khi mọi response khác là camelCase (`CONVENTIONS.md` §1.3), và enum ghi thành SỐ (`"periodType": 0`) trong khi API trả chuỗi — vì `Program.cs` gắn `JsonStringEnumConverter` cho pipeline MVC nhưng `AuditLogService` tự `JsonSerializer.Serialize` với tuỳ chọn mặc định. `metadata` được trả nguyên văn qua `/admin/audit-logs`, nên `0` buộc người đọc log về sau phải tra ngược thứ tự khai báo enum — một thứ tự có thể đã đổi. Không lộ ra trước đây vì chưa handler nào truyền `metadata` và cũng chưa có đường đọc audit log. Đã cho `AuditLogService` dùng đúng cấu hình của API; verify bằng curl: `{"periodType": "Weekly", "conditionType": "ContributeToGoal"}`.

**Verify Phase 10 #4 (2026-09-13):** `dotnet build` sạch 0 warning; `dotnet test` xanh **370/370** (345 → 370); `dotnet ef migrations has-pending-model-changes` sạch. AI Service `pytest` **155/155** (104 → 155); `black --check`/`isort --check-only`/`ruff check` sạch; `alembic check` sạch sau migration `0005_receipt_sample_source`.

`docker compose up --build` — `tesseract --list-langs` trong container trả về `eng`, `osd`, **`vie`**.

**Smoke test curl qua BACKEND:**

- **Voice** — 5 câu có số đọc bằng CHỮ qua `POST /transactions/parse`: "Cà phê Highland bốn mươi lăm ngàn" → 45.000/food; "trưa nay ăn phở hai mươi lăm nghìn ở Phở Thìn" → 25.000/food/merchant "Phở Thìn"; "chuyển cho mẹ hai triệu rưỡi" → 2.500.000; "đổ xăng một trăm năm mươi ngàn ở Petrolimex" → 150.000/transport; "nhận lương tháng chín mười lăm triệu từ công ty ABC" → **15.000.000**/credit/income. Lưu với `source=Voice` → 201; khai `source=Notification` → **400**.
- **OCR** — ảnh hóa đơn thật (siêu thị, 14 dòng, có cả dòng hàng lẫn "Tiền khách đưa") qua tesseract thật: `ocr_result=success`, **110.000đ** (đúng tổng cộng, không phải 200.000 tiền khách đưa), `matched_pattern_name="tong cong"`, ngày 12/09 đọc từ hóa đơn chứ không lấy lúc upload, confidence 0,80. Lưu với `source=Receipt` → 201. File không phải ảnh → 422; không đăng nhập → 401.
- **AI DB** — `pipeline_requests` có dòng `package_name='receipt_ocr'` đủ trường; `raw_samples` source `receipt` với **mã số thuế đã che** (`MST: xxxxxx5678`) còn số tiền giữ nguyên.
- **Log** — grep 0 kết quả cho merchant/số tiền/MST ở log AI Service; không lỗi chưa xử lý ở cả hai service.

**Hai lỗi chỉ lộ ra khi chạy tesseract THẬT, bộ test đơn vị không bắt được:**

1. **Tesseract mặc định (`--psm 3`) cắt hóa đơn thành cột.** Nhãn rời khỏi số của nó — `"Tong cong:"` một dòng, `"000"` một dòng khác — nên không nhãn nào khớp và cả cơ chế bóc tổng tiền theo nhãn trở thành vô dụng. Test đơn vị dùng text hóa đơn viết tay, vốn luôn đúng hàng đúng lối, nên chưa bao giờ chạm tới. Sửa bằng `--psm 6` (một khối văn bản đồng nhất); đã so 5 chế độ, chỉ `--psm 4` và `--psm 6` giữ được `"Tong cong: 110.000"` trên một dòng.
2. **Mã số thuế đọc thành số tiền.** Hệ quả của lỗi 1: không nhãn nào khớp nên rơi vào nhánh đoán "lấy con số lớn nhất", và `MST: 0312345678` thành một giao dịch **312.345.678đ**. Nhánh đoán nay chỉ xét con số TRÔNG GIỐNG TIỀN — có dấu ngăn nhóm hoặc có đơn vị. Người Việt viết tiền là luôn có dấu ngăn, nên dãy số trần dài (mã số thuế, số hóa đơn, số điện thoại) phân biệt được. Đã thêm 3 test hồi quy.

**Giới hạn đã biết, không che giấu:** trên ảnh thử nghiệm, tesseract đọc `WINMART+` thành `WTNMART+` (nhầm I thành T). Tên cửa hàng sai một ký tự là trượt từ điển merchant, nên danh mục rơi về `other` với confidence 0,35 — tức là người dùng phải tự chọn. Đây là suy giảm ĐÚNG hướng (không đoán bừa một danh mục sai), nhưng nó cho thấy độ chính xác thật của OCR tiếng Việt phụ thuộc nhiều vào chất lượng ảnh. Con số thật chỉ có sau khi chụp hóa đơn thật bằng điện thoại thật.

**Verify Phase 11 (2026-09-13):** `dotnet build` sạch 0 warning; `dotnet test` xanh **396/396** (chạy qua container SDK 9.0); `dotnet ef migrations has-pending-model-changes` sạch sau migration `AddMonthlyIncomeToUsers`. Smoke test curl end-to-end trên `docker compose` thật, cả bảy bước:

1. **Cảnh báo tức thì.** Budget Ăn uống 1tr → chi 750k / +200k / +100k: đúng ba thông báo 70% → 90% → 100%, tất cả phát ra **trong chính request `POST /api/v1/transactions`** (log ghi `ActionName: TransactionsController.Create`), không phải chờ đầu giờ. Chi thêm 500k → im lặng.
2. **Nhảy vọt một phát.** Budget Mua sắm 1tr → một giao dịch 1,2tr → đúng **một** thông báo (mốc 100), không phải ba.
3. **Lưới vét vẫn cần thật.** Budget Hóa đơn 2tr, đã chi 750k (37,5% — không cảnh báo), hạ hạn mức xuống 800k qua `PATCH /budgets/{id}` → **không** cảnh báo ngay, đúng như thiết kế (không có giao dịch nào). Chờ đến lượt cron `0 * * * *` lúc 14:00:00 UTC → job bắt được và gửi mốc 90%, log ghi ngoài mọi `ActionName`. Đây là bằng chứng chạy thật cho lý do giữ job lại.
4. **Tab Chờ duyệt.** `?status=Draft` rỗng → `/notifications/analyze` tạo nháp → trả đúng 1 dòng `Draft` → confirm → rỗng trở lại. `?status=Confirmed` trả 6 dòng, toàn bộ đúng trạng thái.
5. **Lời khuyên dự báo.** Chưa khai thu nhập → `advice: null` nhưng `projectedSpendCents` vẫn có. Khai 3.000.000 với dự báo chi 3.585.000, còn 17 ngày → `projectedBalanceCents: -585000`, `suggestedDailyCutCents: 34412` (đúng `ceil(585000/17)`). Gửi `0` → cột về `NULL` và `advice` về `null`. Gửi số âm → 400.
6. **Tính khả thi mục tiêu.** Thu nhập 3tr, tổng hạn mức ngân sách 2,8tr → dư 200k/tháng. Mục tiêu 20tr hạn 6 tháng → HTTP **200** kèm `isFeasible: false`, `requiredPerMonthCents: 3333334`, gợi ý kéo dài hạn tới 2035 hoặc hạ mục tiêu xuống 1,2tr — cảnh báo kèm lối ra, không phải lỗi. Mục tiêu 1tr cùng hạn → `isFeasible: true`.
7. **Provider tắt đúng nghĩa là ẩn.** `GET /financial-accounts/providers` trả 5 provider đang bật; Techcombank/VPBank/ShopeePay nằm trong DB với `is_active = f` và không lọt vào danh sách người dùng thấy.

**Một chỗ lệch tìm thấy khi verify, KHÔNG thuộc phạm vi lượt này:** pattern MB Bank bóc `merchantName` thành `"HIGHLANDS COFFEE. So du 4,915,000 VND"` — regex ăn cả phần số dư đuôi thay vì dừng ở dấu chấm. Không sai số tiền (85.000đ đúng), chỉ bẩn tên cửa hàng, và nó làm hỏng việc khớp từ điển merchant nên đẩy danh mục về `other`. Thuộc `provider_patterns` của AI service, sửa được bằng dữ liệu chứ không cần đụng code.

**Verify Phase 12 (2026-09-14):** `dotnet build` sạch 0 warning; `dotnet test` xanh **421/421**; `has-pending-model-changes` sạch sau migration `AddDeviceTokens`. Chạy thật trên `docker compose` ở CẢ HAI chế độ:

- **Không credentials:** log khởi động `"Push notifications: FCM credentials absent, running log-only."`; `POST /devices` → 204, gọi lại y hệt → vẫn 204 và DB vẫn đúng 1 dòng với `last_seen_at` đã đổi; token rỗng → 400; không đăng nhập → 401; `DELETE /devices` → 204 và dòng biến mất.
- **Có credentials** (service-account tự sinh bằng openssl, dùng xong xoá): log `"Push notifications: FCM enabled."` — chứng minh `CredentialFactory` parse được và DI đã đổi sang `FcmPushNotificationService`. Rồi cho một giao dịch chạm mốc 70% với project giả: **giao dịch vẫn 201 và vẫn được lưu**, log ra `Warning ... reached no device across 1 token(s); FCM said Unknown (Google.Apis.Auth.OAuth2.Responses.TokenResponseException)`, token **không** bị xoá (lỗi xác thực là tạm thời, không phải token chết), và grep toàn bộ log không thấy token xuất hiện lần nào.

**Điều học được khi verify, đã đổi thiết kế theo:** FCM **không ném exception** khi sai credentials — nó trả `BatchResponse` với lỗi theo TỪNG token, `ErrorCode = Unknown`, `MessagingErrorCode = null`, `InnerException = null`, và nhét exception gốc vào phần đầu `Message`. Bản đầu chỉ ghi `Information: 0 delivered, 0 pruned` — đúng loại hỏng im lặng đắt nhất để truy. Bản sau nâng lên `Warning` và lấy tên kiểu ở đầu `Message`, có 4 test ghim lại việc **không bao giờ** lấy phần còn lại của `Message` (khi lỗi thuộc về chính token thì FCM nhắc lại token trong đó).

**Verify Phase 13 (2026-09-14):** `dotnet build` sạch 0 warning; `dotnet test` xanh **449/449**; `has-pending-model-changes` sạch sau migration `AllowCancellingDataDeletion`. Smoke test curl trên `docker compose` thật:

1. **Thăng quyền có hiệu lực.** User thường gọi `/admin/users` → 403; admin `PATCH .../role` thành `Admin`; user **đăng nhập lại** → 200. (Phải đăng nhập lại vì vai trò nằm trong access token.)
2. **Tự đổi vai trò bị chặn** → `ADMIN_CANNOT_CHANGE_OWN_ROLE`. Gọi `PATCH /role` với đúng giá trị đang có → 200 và **không** thêm dòng audit nào.
3. **Chốt "admin cuối cùng" chỉ với tới được qua đúng một đường**, và verify chứng minh nó cần thật: admin bị khoá vẫn cầm access token còn hạn 15 phút, dùng token đó hạ admin hoạt động cuối cùng → `ADMIN_CANNOT_DEMOTE_LAST_ADMIN`, vai trò giữ nguyên. Nếu không có chốt, đúng cửa sổ 15 phút đó là khoá được toàn bộ hệ thống ra ngoài.
4. **Hàng đợi xoá.** User `DELETE /auth/account` → 204, đăng nhập lại **401** (không tự huỷ được — chính là lý do phải có admin). Admin thấy dòng đó với `email`, `status: Pending`, `daysUntilHardDelete: 30`.
5. **Huỷ cứu được người dùng.** `POST .../cancel` → `Cancelled` kèm `processedAt`; user **đăng nhập lại 200**. Huỷ lần hai → 200, vẫn `Cancelled`, không audit thêm.
6. **Audit + phân quyền.** `/admin/audit-logs` có `Admin.User.RoleChanged` (kèm `from`/`to`/`reason`) và `Admin.DataDeletionRequest.Cancelled`. Cả ba endpoint mới trả **403** với user thường.

**Điều verify dạy được, đã sửa theo:** plan ghi đường cứu khi mất hết admin là "sửa env + khởi động lại" — **đường đó không có thật**. `AdminUserSeeder` chỉ hỏi "email này có chưa" rồi bỏ qua, nên admin seed bị hạ quyền thì restart bao nhiêu lần cũng vô ích. Đã sửa seeder thành bảo đảm tài khoản seed thực sự dùng được mỗi lần khởi động, và kiểm chứng bằng restart thật: `role=user` → restart → `role=admin`, đăng nhập 200. Bốn test tích hợp ghim lại (kể cả việc **không** đặt lại mật khẩu).

**Verify Phase 14 (2026-09-14):** `dotnet build` sạch 0 warning; `dotnet test` xanh **457/457**; `has-pending-model-changes` sạch sau migration `AddClientRequestIdToTransactions`. Smoke test curl trên `docker compose` đủ stack (có cả `ai-service`):

1. **Gửi lại giao dịch.** Cùng `clientRequestId`: lần 1 → **201**, lần 2 và 3 → **200**, đúng 1 giao dịch trong danh sách, số dư `49.955.000` (trừ đúng MỘT lần từ 50 triệu). Đây là chỗ đắt nhất nếu sai — trừ tiền hai lần thì số dư lệch mà không có gì báo.
2. **Gửi lại thông báo sau khi cửa sổ cũ hết hiệu lực.** Gửi một thông báo MB Bank với `receivedAt` cố định → `Processed`, tạo 1 nháp. Lùi `created_at` của dòng log về **1 tiếng trước** rồi gửi lại y hệt → vẫn **cùng `logId`**, `draft: null`, 1 dòng `notification_logs`, 1 nháp. Logic cũ (`CreatedAt >= now − 5 phút`) sẽ tạo thêm cả log lẫn nháp.
3. **Rate limit đăng nhập.** Sai mật khẩu liên tiếp: lần 1–10 → **401**, từ lần **11** → **429**.

**Hai điều verify dạy được, đã sửa theo:**
- **Rate limit gắn sai chỗ.** Bản đầu gắn policy mặc định bằng `MapControllers().RequireRateLimiting(...)`. Đo thực tế: đăng nhập sai 13 lần vẫn 401 hết, và 429 chỉ xuất hiện ở request thứ ~106 — tức là `/auth/login` đang chạy ở mức **120** chứ không phải 10, vì metadata do convention thêm vào đè mất `[EnableRateLimiting(AuthPolicy)]`. Chuyển sang `GlobalLimiter` thì hai hạn mức cộng dồn, và 429 rơi đúng lần 11.
- **Một lần báo động giả do kịch bản test của tôi sai, không phải do code.** Lần thử đầu tiên tôi tính lại `receivedAt = now − 6h` trong một shell mới nên nó lệch vài phút so với lần gửi trước → `contentHash` khác → dedup không khớp là ĐÚNG. Phải cố định chuỗi `receivedAt` mới là mô phỏng đúng thông báo nằm sẵn trong Room.

**Vẫn chưa ai làm, nằm ngoài repo này:** phần Room Database trên máy. Backend giờ chịu được cú đồng bộ, nhưng việc dữ liệu có được giữ lại trên thiết bị khi mất mạng hay không thì phải đội Android xác nhận — `android/` không có trong checkout này.

**Verify Phase 15 (2026-09-14):** `dotnet build` sạch 0 warning; `dotnet test` xanh **475/475**; `pytest` xanh **126/126** (unit). Không đổi schema nên không có migration. Smoke test curl trên `docker compose` đủ stack:

1. **Chi, trúng từ điển** (`ND: TT QR HIGHLANDS COFFEE`) → `Xác nhận nhanh` | `"Bạn vừa chi 75.000đ tại HIGHLANDS COFFEE (Ăn uống)?"` — đúng câu của docx, và tiền đúng kiểu Việt. `action=confirm_one_tap`.
2. **Chi, KHÔNG trúng từ điển** (`XYZQWE SHOP`, phân loại 0,35) → `Giao dịch mới cần phân loại` | `"Bạn vừa chi 320.000đ tại XYZQWE SHOP. Chọn danh mục giúp Mascot nhé."` — **không nêu danh mục**. `action=choose_category`.
3. **Thu** (lương 15tr, phân loại 0,95) → một chạm, đọc là `"Bạn vừa nhận 15.000.000đ"`.
4. **Bước 5.3 một lần gọi**: `POST /confirm` kèm `{"categoryId": <Mua sắm>}` → 200, giao dịch chuyển sang Mua sắm, và hạn mức **Mua sắm** tăng 320.000 (danh mục AI đoán là "Khác" **không** bị trừ).
5. **Feedback tới AI**: bảng `user_feedback` của AI DB có dòng `category_correction | other → shopping`.
6. **Nhánh một chạm không gãy**: `POST /confirm` **không body, không content-type** → 200; body `{}` → 200. Client cũ không vỡ.

**Điều test bắt được mà mắt không thấy:** số tiền trong push ra `75,000đ` (dấu phẩy kiểu Mỹ) vì `:N0` lấy culture của tiến trình, mà container chạy `InvariantGlobalization`. Lần sửa đầu dùng `CultureInfo.GetCultureInfo("vi-VN")` thì **ném ngay khi khởi tạo lớp** — culture đó không tồn tại ở chế độ invariant. Phải dựng `NumberFormatInfo` tay. Lỗi này có sẵn từ dòng push cũ của Phase 4, không ai để ý vì chưa có test nào đọc nội dung push.

**Verify Phase 16 (2026-09-15):** `dotnet build` sạch 0 warning; `dotnet test` xanh **493/493**. Smoke test trên `docker compose` thật:

1. **Image**: `2,86GB → 359MB`. `docker compose exec backend id` → `uid=1654(app)`, **không phải root**. Log khởi động không còn dòng biên dịch nào.
2. **Health check thật**: bình thường `live=200 ready=200`; `docker compose stop postgres-main` → **`ready=503`** trong khi **`live` giữ 200**; bật lại → 200. Đúng ý đồ tách hai đường.
3. **Forwarded headers, `TRUSTED_PROXIES=*`** với hạn mức auth 3/phút: `X-Forwarded-For: 203.0.113.1` → 401,401,401,**429,429**; rồi IP khác `203.0.113.99` → 401,401 — **có ngăn riêng**, đúng như mong đợi.
4. **Mặc định an toàn, `TRUSTED_PROXIES` trống**: năm IP giả khác nhau vẫn → 401,401,401,**429,429** — header bị BỎ QUA, tất cả chung một ngăn. Đây là điều quan trọng nhất phải chứng minh: bật bừa còn nguy hơn không bật.
5. **Chốt bí mật**: `ASPNETCORE_ENVIRONMENT=Production` + `JWT_SECRET=change-me...` → container **không khởi động**, log ghi `Refusing to start in Production: ... — JWT_SECRET, AI_SERVICE_API_KEY, HANGFIRE_DASHBOARD_PASS`.
6. **Flow 1 + Flow 2 chạy lại trên image mới**: thông báo → nháp → push `"Bạn vừa chi 75.000đ tại HIGHLANDS COFFEE (Ăn uống)?"` → confirm 200 → số dư 4.925.000; văn bản 45.000/`food`, giọng nói 45.000/`food`, hoá đơn 100.000, form 201; ngân sách 875.000/1.000.000.

**Điều thực nghiệm dạy được:** ngữ nghĩa "tin mọi proxy" của `ForwardedHeadersMiddleware` rất dễ hiểu NGƯỢC — để trống cả `KnownNetworks` lẫn `KnownProxies` KHÔNG phải "tin tất cả" mà là "không tin ai", và header bị bỏ qua trong im lặng. Phải thêm một dải phủ `0.0.0.0/0` và `::/0`. Chỉ đo bằng curl mới biết, đọc code không ra.

**Cảnh báo lúc khởi động còn lại, đều CÓ SẴN từ trước và ngoài phạm vi lượt này:** 7 dòng EF global query filter (`RefreshToken` ↔ `User`); Data Protection key ring không được lưu ngoài container (không ảnh hưởng vì auth dùng JWT bearer, không dùng cookie); `Failed to determine the https port for redirect` từ `UseHttpsRedirection` — vô hại khi TLS do proxy đảm nhiệm.

**Verify Phase 17 (2026-09-15):** `dotnet build` sạch 0 warning; `dotnet test` xanh **512/512**; `has-pending-model-changes` sạch sau migration `AddEmailVerifiedToUsers`. Smoke test curl trên `docker compose` thật (không có key Brevo nên mã đọc từ log — đúng chế độ đã thiết kế, log khởi động ghi `"Email: BREVO_API_KEY absent, OTP codes go to the log instead."`):

1. **Quên mật khẩu**: đăng ký → `forgot-password` 204 → đọc mã `248597` từ log → `reset-password` 204 → đăng nhập bằng mật khẩu **MỚI** 200, mật khẩu **CŨ** 401.
2. **Không lộ tài khoản**: `forgot-password` cho email có thật và email bịa → **cùng HTTP 204, cùng body rỗng 0 ký tự**. Và không có thư nào thật sự gửi tới địa chỉ bịa.
3. **Giới hạn theo email**: gọi `forgot-password` hai lần liên tiếp cùng địa chỉ → cả hai đều 204 (không lộ gì), nhưng **chỉ 1 email thật sự được gửi**.
4. **Cổng xác minh**: đăng ký → đăng nhập ngay → `AUTH_EMAIL_NOT_VERIFIED`. `resend-verification` 204 → đọc mã → `verify-email` 204 → đăng nhập 200.
5. **Mã dùng một lần**: gửi lại chính mã vừa dùng → `AUTH_OTP_INVALID`.

**Cắm Brevo thật (2026-09-15).** Key hợp lệ vẫn trả **401**. Nguyên nhân KHÔNG phải key sai cũng không phải code sai: Brevo mặc định bật **Authorised IPs**, chặn mọi lời gọi từ IP chưa khai báo (`"unrecognised IP address 42.112.230.30"`). Tắt ràng buộc đó xong thì thư đi ngay — Brevo ghi nhận `2026-09-15T10:01:22 'FinMate — Mã đặt lại mật khẩu'`, và `/v3/smtp/emails` xác nhận. **Khi deploy lên server thật, IP của server sẽ bị chặn y hệt** — đây là loại lỗi rất dễ mất cả buổi để truy lại lần hai.

Lỗi này cũng lộ ra một chỗ log chưa đủ tốt: `BrevoEmailSender` chỉ ghi `HTTP 401` nên phải gọi tay sang Brevo mới biết lý do. Nguyên do là tôi cố ý không ghi body phản hồi — ở nhánh THÀNH CÔNG Brevo dội lại payload, mà payload là nội dung email nên chứa cả mã OTP. Nhưng phản hồi LỖI chỉ có `code` và `message`, nên nay bóc đúng hai trường đó theo TÊN, không ghi nguyên body: cách này vẫn an toàn kể cả nếu Brevo đổi và dội payload kèm trong phản hồi lỗi. Có 8 test ghim lại, trong đó một test đưa hẳn mã OTP giả vào body lỗi để chắc nó không lọt ra.

**Chuyện xảy ra khi verify, đáng ghi lại:** giữa chừng mọi request trả **429**. Không phải lỗi — chính rate limit `auth` 10/phút của Phase 14 đang làm đúng việc, và tôi đã đốt hết hạn mức bằng các phép thử trước đó. Phải chờ cửa sổ trôi qua rồi chạy lại. Đây cũng là bằng chứng phụ rằng hai phase gắn đúng vào nhau.

**Verify Phase 18 (2026-09-15):** `dotnet build` sạch 0 warning; `dotnet test` xanh **537/537**. Không đổi schema. Trên `docker compose` thật (chưa đặt `GOOGLE_CLIENT_SECRET`):

1. Log khởi động: `"Google OAuth (browser flow): GOOGLE_CLIENT_SECRET/REDIRECT_URI absent, disabled."`
2. `GET /auth/google/start` → **422** `AUTH_GOOGLE_OAUTH_NOT_CONFIGURED` — nói rõ, **không phải 500**.
3. `POST /auth/google` (đường native) → 401 với token giả, tức là **vẫn sống**.

Phần chạy với tài khoản Google thật cần user tạo client secret và khai Redirect URI; 10 test tích hợp đã phủ toàn bộ luồng bằng bộ đổi-code giả, gồm cả ca state giả mạo, state phát lại, huỷ ở màn Google, và mã bàn giao dùng lần hai.

**Cắm Google thật (2026-09-15) — lỗi chỉ chạy thật mới lộ.** Lần đầu callback báo "Không xác thực được với Google", trong khi log ghi `HTTP 200 | Error: None | Description: None` — Google trả về hoàn toàn bình thường mà backend vẫn coi là thất bại. Nguyên nhân: Google gửi trường **`id_token`** (snake_case) còn record khai `IdToken`, mà bộ đọc JSON mặc định chỉ khớp camelCase — nên đọc ra null. Hỏng IM LẶNG đúng nghĩa: không có mã lỗi nào, không có exception nào. Sửa bằng `[JsonPropertyName]` tường minh.

**Vì sao 537 test không bắt được:** test tích hợp thay nguyên `IGoogleCodeExchanger` bằng bản giả để khỏi gọi Google thật, nên tầng HTTP và JSON chưa bao giờ được chạy. Đã thêm 3 test cho riêng phần đọc JSON, trong đó một test khẳng định NGƯỢC LẠI (`idToken` camelCase **không** được đọc) để ai sửa ngược lại thì test đỏ chứ không xanh một cách sai lầm.

**Chạy thật trọn vẹn sau khi sửa:** `/start` → đăng nhập Google thật → trang hiện mã bàn giao → `/exchange` trả JWT → `GET /users/me` trả đúng `thiennguyen.csnt@gmail.com`. DB xác nhận `google_id` đã gắn vào tài khoản email/mật khẩu sẵn có (auto-link) và `email_verified_at` được đặt tự động như Phase 17 thiết kế. Dùng lại mã bàn giao lần hai → `AUTH_TOKEN_INVALID`.

**Điều thực nghiệm dạy được:** ASP.NET chuẩn hoá URI redirect nên `finmate://auth?code=…` ra thành `finmate://auth/?code=…` (thêm một dấu gạch chéo). Vô hại với intent filter của Android vì nó khớp theo scheme + host, nhưng đã ghi vào test để phía Flutter không mất thời gian truy khi thấy đường link khác chuỗi cấu hình.

## Phase 27 — Voice: giọng nói → chữ → luồng chat (2026-10-13)
Lấy phần xử lý voice của gói Voice Lab (STT GIPFormer ONNX) gắn vào hệ thống: **không** mang theo luồng OpenRouter/privacy riêng.
- `stt-service/` (mới, nội bộ): `POST /transcribe` audio → chữ thường. Model ~258MB đặt tay ở `stt-service/models/gipformer1.5-fp32/` (git bỏ qua, xem README).
- Backend: `POST /api/v1/transactions/voice` (multipart `file`) → `ISttServiceClient` → `IChatCommandHandler` (cùng luồng gõ chữ: nhiều khoản, ngày, danh mục chung, trùng lặp, xác nhận). Trả `{transcript, chat}`.
- Cấu hình: `STT_SERVICE_URL` (compose), `STT_SERVICE_API_KEY` (backend/.env = stt-service/.env). Thiếu URL → endpoint trả lỗi AI service không khả dụng.
- Đã chạy thật: 3 file mp3 (edge-tts) qua BE → "hôm qua ăn phở bốn mươi nghìn rồi đổ xăng bảy mươi nghìn" ra 2 nháp 40.000 food + 70.000 transport. Chưa đo WER/độ chính xác trên giọng thật. App Flutter chưa có nút ghi âm.

## Phase 28 — Vòng học: huấn luyện và phục vụ mô hình nhóm hóa đơn (2026-10-16)
Hoàn tất Task 6-8 của `docs/superpowers/plans/2026-10-13-learning-loop.md` (Task 1-5 đã xong trước đó). Mô hình **tắt** cho đến khi có đủ nhãn thật.
- `app/bill/slug_model.py`: cổng mẫu dùng chung với `learning_report.py`; `train()` ra `candidate` chỉ khi CV theo cửa hàng ≥ 0,85 và hơn luật; `record_training` / `promote` / `retire`; `SlugModel` (tệp hỏng thì bỏ qua); `load_active` khi khởi động.
- `scripts/train_bill_model.py` (`--train | --promote <v> | --retire | --list`), `scripts/eval_category_dataset.py` (đo trên CSV, tách nhãn thật khỏi tổng hợp).
- Phục vụ: `/bill/classify` thử mô hình **sau** bộ nhớ, thương hiệu, từ khóa, bình chọn món, và chỉ khi luật trả `none` và xác suất ≥ 0,8 (`source = "model"`, độ tin cậy ≤ 0,8). Không dùng trong `classify_items` (mô hình học trên chữ cả hóa đơn, không phải từng món).
- Runbook: `docs/learning-loop.md`. Quy trình đã chạy thử trọn vẹn trên DB tạm với dữ liệu giả.
- Số đo baseline (`docs/evaluation.md`): mô hình chưa hơn luật; học trên dữ liệu tổng hợp rồi đo trên thật chỉ đúng 56,5%. Cần nhãn thật từ người dùng đóng góp.
- Chưa làm: không có endpoint nạp lại mô hình (promote xong phải khởi động lại ai-service); thu mẫu thật từ người dùng thử.

## Phase 29 — API quản trị luật phân loại của AI (2026-10-16)
Để màn hình admin trên web (làm riêng) chỉnh được luật mà không phải sửa code hay build lại. Mới có API, chưa có giao diện. Tài liệu cho người dựng giao diện: `docs/ai-rules-admin-api.md`.
- AI Service: `GET/PUT/PATCH/POST/DELETE /api/v1/admin/rules/*` (khóa nội bộ + tiêu đề `X-Actor-Hash` cho thao tác ghi) cho thương hiệu, từ khóa hóa đơn (merchant/product), bộ nhớ tên món, chỉ dẫn Gemini; `/preview` thử luật trước khi lưu bằng chính hàm của `/bill/classify` (`classify_by_rules`); `/history` + `/history/{id}/revert`. Bảng mới `bill_rule_audit` (migration 0014). Mọi thay đổi có hiệu lực ngay (đánh dấu bộ kiến thức cũ, bỏ cache chỉ dẫn), không đợi nhịp 60 giây.
- Backend: `AdminAiRulesController` dưới `AdminOnly`, `AiRulesAdminClient` (HttpClient + khóa nội bộ, băm id admin), `AdminAiRulesService` ghi `audit_logs` (`Admin.AiRule.Changed`) CHỈ khi AI Service chấp nhận. Lỗi 404/409/422 của AI Service giữ nguyên mã và thông điệp, lỗi khác là 503.
- Chỉ chỉnh được DỮ LIỆU luật: từ điển chat và logic (đọc số tiền/ngày, chọn tên cửa hàng/tổng tiền trên OCR) vẫn nằm trong code; thương hiệu `family`/`education` ánh xạ trong code.
- Chưa làm: tự chạy bộ đo hồi quy trước khi áp dụng; cảnh báo xung đột "từ khóa là một phần của từ khóa dài hơn ở nhóm khác"; giao diện web; hàng đợi "món bị người dùng sửa nhiều nhất" (cần thu nhãn sửa của người dùng).

## Phase 30 — Gộp đồng ý AI vào Điều khoản (2026-10-17)
Người dùng chỉ đồng ý MỘT lần (Điều khoản + Chính sách, bản 2), không còn bảng hỏi riêng khi dùng chat, giọng nói hay quét bill.
- Backend: `ConsentVersions.Terms = 2` (mọi người dùng đồng ý lại bản mới); `ConsentCommandHandler` bật `AiConsentVersion` khi đồng ý Điều khoản, trừ người đã chủ động tắt (`AiOptedOut = 0`). Rút AI ghi `AiOptedOut`. Không cần migration (cột `int?` có sẵn).
- App: màn Điều khoản có thêm đoạn "Phân loại chi tiêu bằng AI" (gửi gì, không gửi gì, bên nhận, tắt ở đâu); xóa `ai_consent_screen.dart` và mọi `ensureAiConsent`; Hồ sơ có công tắc "Phân loại chi tiêu bằng AI" (mặc định bật). Tắt AI thì quét bill/chat/giọng nói vẫn chạy bằng luật.
- Không đổi: đóng góp dữ liệu huấn luyện vẫn là đồng ý riêng, tắt mặc định.
- **Câu chữ Điều khoản bản 2 do trợ lý soạn, chủ dự án chưa duyệt; nên có người am hiểu pháp lý xem lại trước khi phát hành.** Cũng nên kiểm tra điều khoản dùng dữ liệu của gói Gemini/OpenRouter đang dùng.

## Phase 31 — Chat dùng Gemini riêng, nhanh và chịu giới hạn (2026-10-17)
- Client Gemini riêng cho chat/giọng nói (xem ARCHITECTURE): model nhanh trước, trần thời gian lồng nhau (3,5 / 7,5 / 9 giây, cấu hình bằng `CHAT_GEMINI_*` và `CHAT_AI_TIMEOUT_SECONDS`), cầu dao tách khỏi hóa đơn. Hóa đơn giữ chuỗi model cũ.
- Đo thật: khóa hiện dùng bị 429 dày khi gọi liên tiếp (model chính 30/30, lite 8/30). Việc cần làm tiếp phía vận hành: bật gói trả phí cho Gemini, và thay model dự phòng OpenRouter `:free` bằng model trả phí hoặc bỏ.
- Ghi chú: `tests/integration/test_rules_admin.py::test_set_brand_active_toggles_and_unknown_is_404` hỏng một lần trong lượt chạy toàn bộ rồi không tái hiện (5 lần chạy sau đều xanh); nghi ngờ phụ thuộc thứ tự thời gian của dòng nhật ký.

## Phase 32 — Ràng buộc DB mức Cao của AI Service (2026-10-24)
Làm phần "Cao" của `docs/ai-service-db-constraints-v5_1.md`.
- Migration `0020_fk_indexes`: 4 chỉ mục cho khóa ngoại còn thiếu (`pipeline_requests.raw_sample_id`, `model_versions.training_job_id`, `user_feedback.batch_job_id` — từng phần `IS NOT NULL` — và `evaluation_predictions.labeled_sample_id`), khai báo song song trong ORM để `alembic check` không lệch.
- `scripts/purge_pipeline_requests.py`: xóa `pipeline_requests` cũ hơn 90 ngày, theo lô 5000, có `--days` và `--dry-run`; chạy bằng cron ngoài như hai script dọn còn lại. Mốc 90 ngày do chủ dự án quyết định.
- Test: `tests/integration/test_purge_pipeline_requests.py` (mốc hạn, dry-run, nhiều lô, chỉ mục tồn tại).
- Chưa kiểm chứng chạy thật: máy hiện chỉ có Python 3.10 không có thư viện dự án và không có Postgres, nên mới biên dịch file (py_compile), chưa chạy pytest/`alembic upgrade head`/`alembic check`.

## Phase 33 — ERD v5_1 giai đoạn 1: đổi tên bảng, thêm cột và ràng buộc (2026-10-24)
Chưa gộp bảng. Migration `0021_erd_v5_1_phase1` (có downgrade).
- Đổi tên 14 bảng: `bill_brands`→`kb_brands`, `bill_keywords`→`kb_keywords`, `bill_item_cache`→`kb_item_categories`, `bill_ai_instructions`→`kb_ai_instructions`, `provider_patterns`→`kb_provider_patterns`, `bill_rule_audit`→`kb_audit_log`, `bill_user_merchant_choice`→`user_merchant_memory`, `pipeline_requests`→`ops_analyze_log`, `bill_classify_stats`→`ops_usage_daily`, `user_feedback`→`ml_user_feedback`, `raw_samples`→`ml_raw_samples`, `feedback_batch_jobs`→`ml_feedback_batches`, `sample_splits`→`ml_sample_splits`, `bill_training_samples`→`ml_training_samples`. Cột `ml_user_feedback.batch_job_id`→`batch_id`. Tên lớp ORM và tên chỉ mục/ràng buộc cũ giữ nguyên.
- Cột mới: `ops_analyze_log` (`categorizer_fallback_reason`, `pattern_id`, `config_versions`, `extra_fields`), `kb_brands.mixed`, `kb_item_categories` (`source`, `ai_model`), `ml_training_samples.sample_unit`.
- Ràng buộc mới: 24 CHECK (khoảng [0,1] và không âm, tập giá trị đóng, khóa viết thường, độ dài), khóa ngoại tự tham chiếu `ops_analyze_log.duplicate_of_request_id` (SET NULL, kèm dọn con trỏ treo trước khi thêm), 4 chỉ mục. CHECK thêm NOT VALID rồi VALIDATE: dữ liệu cũ vi phạm thì migration dừng và nêu tên ràng buộc.
- `ocr-service` ĐỌC/GHI TRỰC TIẾP `kb_brands` và `kb_keywords` (storage/knowledge_pg.py, promotion_pg.py): phải triển khai cùng lúc với migration này, nếu không nó lỗi "relation does not exist".
- Quản trị viên sửa bộ nhớ tên món giờ ghi `source = 'admin'` (trước đây không có cột).
- Chưa làm (cố ý): CHECK mã băm hex 64 (nhiều test dùng mã băm giả như "a", "short"); bỏ `total_amount`/`label_kind` của `ml_training_samples` (code đang dùng); bỏ CHECK `transaction_type`/`extraction_method` của `ops_analyze_log` (F1-09, chờ tính năng định dạng); mọi cột/ràng buộc của bảng sẽ bị gộp (`training_jobs`, `model_versions`, `evaluation_*`, `labeled_samples`, `bill_*` cặp đôi) để làm cùng đợt gộp.
- Khác ERD: `kb_audit_log.resource` giữ `item_cache` (code đang ghi), ERD ghi `item`.
- Đã chạy trên Postgres thật (embedded): `alembic upgrade head`, `alembic check` sạch, downgrade 0020 rồi upgrade lại, `pytest` ai-service 1433 qua, ocr-service 28 qua (bằng pg_fake, không phải DB thật).

## Phase 34 — ERD v5_1 giai đoạn 2: `kb_categories` và khóa ngoại `category_slug` (2026-10-24)
Migration `0022_kb_categories` (có downgrade). Chốt phương án B cho 9 nhóm cũ của bộ luật bill.
- Bảng `kb_categories(slug, label, taxonomy_version, is_active, replaced_by, synced_at, created_at)` với CHECK slug `^[a-z_]+$`, `taxonomy_version >= 1`, `replaced_by <> slug`. Seed 11 slug backend (dòng chính) + 4 dòng bí danh không hoạt động cho nhóm cũ không trùng slug backend: `dining`→`food`, `groceries`→`food`, `utilities`→`bills`, `healthcare`→`health`. Năm nhóm còn lại (`transport`, `shopping`, `housing`, `entertainment`, `other`) trùng slug backend nên là dòng chính.
- 9 khóa ngoại `ON UPDATE CASCADE ON DELETE RESTRICT`: `kb_brands`, `kb_keywords`, `kb_item_categories`, `user_merchant_memory` (`category_slug`), `ml_training_samples` (`predicted_slug`, `final_slug`), `ops_analyze_log` (`category_slug`), `ml_user_feedback` (`predicted_category`, `corrected_category`). Thêm NOT VALID rồi VALIDATE: slug lạ trong dữ liệu cũ làm migration dừng, phiên bản giữ nguyên.
- CÁC CHECK liệt kê slug của bảng chỉ nhận slug backend (`kb_item_categories`, `user_merchant_memory`, `ml_training_samples.final_slug`, `ops_analyze_log`, `ml_user_feedback`) được GIỮ: khóa ngoại một mình để dòng bí danh (`dining`...) lọt vào. Bỏ chúng khi từ vựng cũ đã gỡ.
- Thêm danh mục backend mới: thêm slug vào `CATEGORY_SLUGS` và nhãn vào `LABELS` (`app/db/category_seed.py`), thêm CHECK/migration như trước, và INSERT một dòng vào `kb_categories`. Test `test_category_seed` đỏ nếu seed trong code lệch với bản sao cố định trong migration. CHƯA có đồng bộ tự động từ backend (`synced_at` mới chỉ là cột).
- Chưa làm: khóa ngoại cho `labeled_samples`/`bill_labeled_samples` (sẽ gộp ở giai đoạn 3), `ml_label_events`, `ml_item_feedback` (giai đoạn 4); cột `kind`/cờ chi tiêu của `kb_categories` (CHECK `<> 'income'` hiện còn nằm ở từng bảng).
- Đã chạy trên Postgres thật: nâng cấp DB đang có dữ liệu nhóm cũ, đường lỗi (slug lạ), hạ cấp/nâng lại, `alembic check` sạch, `pytest` 1443 qua.

## Phase 35 — ERD v5_1 giai đoạn 3: `ml_models` và gộp bảng `bill_*` vào bộ `ml_*` (2026-10-24)
Migration `0023_ml_models_merge` (có downgrade, đã thử nâng–lùi–nâng trên dữ liệu thật).
- Bảng `ml_models(code UQ, kind sklearn|rasa|llm|rules, provider, notes)`. `code` GIỮ đúng giá trị `stage` cũ: `classifier`, `categorizer`, `bill_slug`, `bill_category` (và tự thêm mọi `stage` lạ có trong dữ liệu cũ). Model mới: INSERT một dòng vào `ml_models` rồi ghi phiên bản với `model_id` (`model_repo.model_id_for`); chưa đăng ký thì báo lỗi, không tự tạo.
- Gộp (hàng giữ nguyên `id`): `training_jobs`+`bill_training_jobs`→`ml_training_jobs`; `model_versions`+`bill_model_versions`→`ml_model_versions`; `evaluation_*`+`bill_evaluation_*`→`ml_evaluation_*`; `labeled_samples`+`bill_labeled_samples`→`ml_labeled_samples`; `bill_raw_samples`→`ml_raw_samples` (mẫu hóa đơn giữ `source = 'bill_scan'`). Sáu bảng `bill_*` đã bị xóa; còn `bill_user_merchant_category` và `bill_unknown_scans`.
- Cột `stage` thành `model_id`. `ModelVersion.stage` và `TrainingJob.stage` vẫn đọc/lọc được (thuộc tính tính từ `ml_models.code`), nên mã dùng `ModelStage` giữ nguyên. `model_repo.list_active` và `/stats` chỉ lấy model của pipeline thông báo (`ModelStage`), vì bảng nay chung với model hóa đơn.
- Thống nhất giá trị: trạng thái phiên bản `archived`→`retired` (`ModelStatus.RETIRED`, thêm `REJECTED`); job `finished` (bill)→`succeeded`; loại giao dịch của bill `expense`→`debit` (ocr-service ghi `debit`); evaluation nhận thêm split `cv`; `labeled_by` là hợp hai bên; CHECK slug của nhãn thay bằng khóa ngoại `kb_categories` (nhóm cũ của bill là dòng bí danh).
- Thêm: `prompt_hash`, `dataset_hash` (CHECK hex 64), UQ `(model_id, version)`, UQ từng phần một bản active mỗi model, UQ `(run, mẫu)` cho dự đoán, CHECK khoảng [0,1] và không âm, chỉ mục `idx_ml_labeled_samples_gold`.
- **ocr-service phải triển khai cùng lúc**: nó ghi thẳng `ml_raw_samples`, `ml_labeled_samples` (đọc chỉ `source = 'bill_scan'`), `ml_training_jobs`, `ml_model_versions`, `ml_evaluation_*` (đã sửa SQL và `tests/pg_fake.py`). Đã chạy SQL của nó trên Postgres thật.
- Chưa làm (cố ý): đổi `source = 'bill_scan'` thành `receipt` như ERD ghi (đang giữ `bill_scan` để phân biệt với văn bản OCR chưa nhãn ở `/ocr` và không phải đổi ocr-service); gộp `nlu_model_versions` vào `ml_model_versions`; gộp `ml_sample_splits` vào `ml_labeled_samples`; `ml_label_events` và `ml_item_feedback` (giai đoạn 4).
- Kiểm chứng trên Postgres thật: migration, `alembic check` sạch, downgrade về 0022 trả đủ số hàng, `pytest` ai-service và ocr-service (xem báo cáo cuối).

## Phase 36 — ERD v5_1 giai đoạn 4: `ml_label_events`, `ml_item_feedback`, view đồng thuận (2026-10-24)
Migration `0024_label_events_item_feedback` (có downgrade).
- `ml_label_events`: lịch sử nhãn của mẫu, mỗi lần đặt/đổi nhãn là một dòng mới (`labeled_sample_id`, `category_slug`→`kb_categories`, `is_financial`, `source` feedback|gold|import|user|system, `feedback_id`→`ml_user_feedback` SET NULL, `actor_hash` hex 64). `ml_labeled_samples` vẫn giữ nhãn HIỆN TẠI; nhãn hiện tại phải bằng sự kiện mới nhất (KHÔNG có ràng buộc DB bảo đảm điều này, chỉ có quy ước: mọi nơi ghi nhãn thêm sự kiện). Migration chép mỗi nhãn cũ thành một sự kiện đầu tiên (nguồn suy từ `labeled_by`, `feedback_id` trống).
- Trigger `ml_label_events_append_only` chặn UPDATE và DELETE TRỰC TIẾP; thay đổi dây chuyền do khóa ngoại (xóa mẫu có nhãn, xóa phản hồi làm `feedback_id` NULL, đổi slug danh mục) vẫn chạy nhờ `pg_trigger_depth() >= 2`, để quyền xóa dữ liệu của người dùng không bị cản. Superuser/chủ bảng vẫn có thể `ALTER TABLE ... DISABLE TRIGGER`; muốn chặn tuyệt đối phải thu quyền của vai trò ứng dụng.
- Nơi ghi sự kiện: `sample_repo.add_label_event` (gọi ở `scripts/feedback_batch.py` với `feedback_id` và `actor_hash`, và `scripts/seed_dataset.py` với nguồn `import`), và ocr-service (`storage_pg.PgDataset.append`, nguồn suy từ `label_source`). `scripts/datasets_pg` chỉ đổi `is_gold`/tên cửa hàng, không đổi danh mục nên không ghi sự kiện.
- `ml_item_feedback((item_key, user_id_hash), category_slug, created_at)`: một phiếu mỗi người mỗi món; CHECK khóa 3-60 ký tự và viết thường, mã băm hex 64, danh mục thuộc slug backend trừ `income` (cùng tập `kb_item_categories`, nên dòng bí danh của nhóm cũ không vào được). KHÔNG có khóa ngoại tới `kb_item_categories` (món có thể chưa có trong kiến thức). CHƯA có chỗ nào trong code ghi bảng này.
- View `v_merchant_consensus` (từ `ml_training_samples`, chỉ `sample_unit = 'bill'` đã có nhãn cuối, mỗi người một phiếu = nhãn MỚI NHẤT của người đó) và `v_item_consensus` (từ `ml_item_feedback`): cột `users` và `agreement` (tỉ lệ phiếu, các dòng của một cửa hàng/món cộng lại bằng 1). View chỉ ĐẾM; ngưỡng (đề xuất 3 người và 80%) và việc bỏ qua cửa hàng `kb_brands.mixed` do nơi dùng quyết định. Chưa có script nào đọc view để đưa cửa hàng/món vào `kb_brands`/`kb_item_categories`.
- Chỉ mục mới `idx_ml_training_samples_merchant_slug` cho view. SQL của view và trigger nằm ở `app/db/ml_sql.py` (dùng chung migration và test, như `bill_views.py`); conftest dựng chúng sau `create_all`.
- Chưa làm: bỏ `bill_merchant_votes`/`bill_unknown_merchants` và hai bảng nguồn của chúng (còn chờ quyết định giữ hàng đợi cửa hàng lạ); trigger chỉ-ghi-thêm cho `kb_audit_log`.
- Kiểm chứng trên Postgres thật: migration có dữ liệu cũ, hạ/nâng lại, `alembic check` sạch, ai-service 1463 test qua, ocr-service 618 test qua (DB giả), và SQL ghi sự kiện của ocr-service chạy đúng trên Postgres thật. `seed_dataset` ghi sự kiện nhưng chưa có test riêng cho script này.

## Phase 37 — ERD v5_1 giai đoạn 5: định dạng thông báo theo nhà cung cấp (2026-10-25)
Migration `0025_provider_formats` (có downgrade). Đây là TÍNH NĂNG, không chỉ lược đồ.
- Ba bảng: `kb_provider_formats` (UQ `(package_name, version)`, status draft|active|archived, `priority`, `created_by` hex 64), `kb_provider_format_fields` (khai báo trường: `maps_to` một trong 6 trường hợp đồng hoặc NULL = trường phụ, `source_kind` regex_group|literal, `is_sensitive`; UQ `(format_id, field_key)` và UQ từng phần `(format_id, maps_to)`), `kb_provider_format_samples` (`sample_text` + `expected_fields` jsonb). `kb_provider_patterns.format_id` NOT NULL, FK RESTRICT; tên pattern chỉ duy nhất TRONG một định dạng.
- Migration chép mỗi `package_name` hiện có thành định dạng v1 (`active` nếu còn pattern bật, ngược lại `archived`), dựng khai báo trường từ named group thật sự dùng và mẫu từ `sample_text` (chạy chính bộ khớp để lấy giá trị mong đợi; pattern không khớp mẫu của nó thì bỏ mẫu đó). `seed_patterns` (khởi động) gắn pattern mặc định vào định dạng v1 `active`, kèm trường và mẫu.
- Bộ khớp: chỉ nạp pattern của định dạng `active` (`app/services/provider_formats.load_specs`), thử theo `(priority định dạng, priority pattern, tên)`; nhiều bản cùng active được. Định dạng có thể ánh xạ trường hợp đồng sang group khác (`amount_cents` lấy từ group `tien`...) và khai báo trường phụ (group hoặc hằng số) ghi vào `ops_analyze_log.extra_fields`; trường `is_sensitive` không bao giờ vào đó, và chữ trường phụ còn đi qua `anonymize_body` (che số dài) như lớp bảo vệ thứ hai. `ops_analyze_log.pattern_id` nay được ghi (pattern đã khớp).
- CỔNG KÍCH HOẠT (`activate`): `draft` chỉ thành `active` khi có ≥ 1 pattern bật, có ≥ 1 mẫu, mọi regex biên dịch được, mọi trường `regex_group` trỏ tới group có thật, và MỌI mẫu khớp `expected_fields` (chỉ so các khóa có mặt) với đủ trường bắt buộc. Ghi `kb_audit_log` (`resource = 'provider_format'`, hành động create/activate/deactivate). Cổng ở tầng ứng dụng, không phải ràng buộc DB: UPDATE status bằng SQL vượt được nó.
- Công cụ vận hành `scripts/provider_format.py` (list | clone <package> | check <id> | activate <id> | archive <id>): sửa regex bằng cách clone bản mới nhất thành draft, sửa, check, activate; bản đang chạy không bị đụng tới. CHƯA có API quản trị qua HTTP cho định dạng.
- Sửa lỗi phát hiện khi làm: gán `None` vào cột JSONB lưu JSON `null` (vi phạm CHECK `jsonb_typeof = object` của giai đoạn 1); `ops_analyze_log.extra_fields` và `config_versions` nay dùng `JSONB(none_as_null=True)`.
- Chưa làm (cố ý): CHECK `NOT (is_sensitive AND maps_to IS NULL)` (cấm luôn việc khai báo trường nhạy cảm, chờ xác nhận đặc tả F1-04); bỏ `provider_key`/`package_name` khỏi `kb_provider_patterns` (còn lặp với định dạng, cổng kiểm chúng khớp nhau); `normalizer`/`normalizer_arg` mới được lưu và kiểm danh sách trắng, `amount`/`balance` luôn dùng bộ chuẩn hóa số tiền mặc định; FK `ops_analyze_log.pattern_id`.
- Kiểm chứng trên Postgres thật: migration có pattern cũ, hạ/nâng lại, `alembic check` sạch, ai-service 1485 test qua, công cụ dòng lệnh chạy đủ vòng clone/check/activate/archive. ocr-service không dùng bảng này nên không đổi.

## Phase 38 — Danh mục: đồng bộ từ backend và loại danh mục (`kind`) thay CHECK liệt kê slug (2026-10-25)
Làm hai mục rủi ro cao/trung bình còn lại của ERD v5_1. Migration `0026_category_kind` (có downgrade).
- **Loại danh mục.** `kb_categories.kind` = `expense` | `income` | `legacy` (dòng bí danh của 9 nhóm cũ). Bảy CHECK liệt kê slug (`kb_item_categories`, `user_merchant_memory`, `ml_training_samples.final_slug`, `ops_analyze_log`, `ml_user_feedback` x2, `ml_item_feedback`) được thay bằng MỘT hàm trigger `kb_require_category_kind` (`app/db/category_guard.py`): khóa ngoại lo "slug có tồn tại", trigger lo "đúng loại" (bảng chỉ nhận chi: `expense`; bảng nhận mọi slug backend: `expense,income`; không bảng nào nhận `legacy`). Thêm danh mục backend = INSERT một dòng, không còn sửa CHECK. Đổi `kind` của danh mục đang có dữ liệu không kiểm lại dòng cũ (API đồng bộ chặn việc đó).
- **Đồng bộ từ backend.** `PUT /api/v1/admin/categories/sync` (+ `GET /api/v1/admin/categories`), khóa nội bộ + `X-Actor-Hash`: cộng thêm, không xóa, tắt bằng `is_active = false`; 409 khi trùng nhóm cũ hoặc đổi `kind`; ghi `kb_audit_log` (`resource = 'category'`). Hợp đồng chi tiết: `docs/ai-categories-sync-api.md`.
- **Bộ nhớ danh mục lúc chạy** (`app/services/category_registry.py`): tập slug hợp lệ đọc từ `kb_categories`, nạp lúc khởi động và mỗi 60 giây (vòng nền trong `lifespan`, vì các schema kiểm slug chạy TRƯỚC handler), nạp ngay ở bản nhận lệnh đồng bộ. Thay hằng `CATEGORY_SLUGS`/`BILL_CHOICE_SLUGS` ở các chỗ nhận slug từ người dùng/backend: phản hồi (`feedback.py`), bộ nhớ cửa hàng (`bill.py`), bộ nhớ tên món (`item_classifier.py`, `chat.py`), schema và nhãn cuối của bill (`schemas/bill.py`, `samples.py`), API quản trị bộ nhớ tên món. Chưa nạp được DB thì dùng `CATEGORY_SLUGS` như trước.
- **Cố ý KHÔNG đổi:** mọi chỗ mô hình/Gemini/từ điển TRẢ RA slug (`categorizer.py`, `gemini.py`, `remote_ocr.py`, `chat_ai.py`, `category_resolver.py`, `config_documents.py`): chúng chỉ đoán các lớp đã học / liệt kê trong code, nên danh mục mới chỉ máy đoán được sau khi huấn luyện lại và thêm vào `CATEGORY_SLUGS`/`LABELS`.
- **Backend đã gọi endpoint này** (2026-10-25): `AiCategorySyncService` + `AiCategorySyncJob` (mỗi giờ + một lần lúc khởi động), ba handler danh mục hệ thống gọi `TrySyncAsync` sau khi ghi audit; cờ `AI_CATEGORY_SYNC_ENABLED` (mặc định bật; `AuthApiFactory` đặt `false`). 741 unit test backend qua; test tích hợp (Docker) và chạy với AI Service thật chưa chạy được ở đây.
- Kiểm chứng trên Postgres thật: migration có dữ liệu cũ, hạ (dừng nếu đã có danh mục ngoài danh sách cũ) và nâng lại, `alembic check` sạch, ai-service 1514 test qua.

## Phase 39 — ERD v5_1: ràng buộc còn lại, receipt, dọn cột, API quản trị định dạng, gộp phiếu chọn cửa hàng (2026-10-26)
Làm hết các mục "chưa làm" của Phase 33-37 theo yêu cầu. Ba migration có downgrade (`0027`, `0028`, `0029`), có test chạy alembic thật trên dữ liệu cũ (`test_migration_0027_0029.py`).
- **0027 `integrity_hardening`.** CHECK mã băm hex 64 cho 11 cột còn thiếu (danh sách ở `docs/ai-service-db-constraints-v5_1.md`; `nlu_model_versions.dataset_hash` cho thêm `'unknown'`); mã người dùng `local` của ocr-service trong `ml_raw_samples` được BĂM LẠI trước khi kiểm. Trigger chỉ-ghi-thêm cho `kb_audit_log` (`app/db/audit_sql.py`; TRUNCATE vẫn được, test dọn bảng bằng nó). FK `ops_analyze_log.pattern_id` -> `kb_provider_patterns` SET NULL + chỉ mục từng phần. F1-04: `NOT (is_sensitive AND maps_to IS NULL)`. F1-09: bỏ CHECK `transaction_type`/`extraction_method` của `ops_analyze_log`.
- **0028 `receipt_source_and_column_cleanup`.** `ml_raw_samples.source` `bill_scan` thành `receipt` (hàng ocr-service giữ `package_name = 'bill_scan'`, nên bước lùi biết hàng nào trả về); bỏ `ml_training_samples.total_amount`/`label_kind` (nhãn xác nhận hay sửa suy từ `predicted_slug` so với `final_slug`; API `/bill/samples` vẫn nhận `total_amount` nhưng không lưu); bỏ `provider_key`/`package_name` khỏi `kb_provider_patterns` (chỉ định dạng giữ chúng; cổng kích hoạt bỏ kiểm "khớp nhau").
- **0029 `merchant_choice_merge`, hướng B.** `bill_user_merchant_category` + view `bill_merchant_votes` GỘP vào `user_merchant_memory` (nhóm cũ đổi qua `kb_categories.replaced_by`; bản có sẵn của người dùng thắng) rồi xóa; bước lùi dựng lại bảng RỖNG. `bill_unknown_scans` GIỮ làm hàng đợi duyệt (bảng DUY NHẤT ngoài ERD, có chủ đích), thêm CHECK hex 64 và `scripts/purge_unknown_scans.py` (90 ngày, cron ngoài).
- **ai-service, bỏ nhánh bảng cũ.** `DbBillKnowledge.remembered` trả `str | None` (slug backend); bỏ tham số `remembered` của `classify_by_rules`/`classify_category_detailed`; xóa model `BillUserMerchantCategory`.
- **ocr-service** (phải triển khai CÙNG LÚC với migration): ghi `source = 'receipt'`, `package_name = 'bill_scan'`; mã người dùng mặc định được băm; `PgMerchantMemory` ghi/đọc `user_merchant_memory` qua bảng nhóm <-> slug backend của `category_groups.json`. **Mất thông tin:** đọc lại `food` ra nhóm `dining` (không phân biệt `groceries`), slug không thuộc 9 nhóm (education, family) bị bỏ qua. `promotion_pg` giờ đọc phiếu từ view `v_merchant_consensus` (bill người dùng ĐÃ ĐỒNG Ý đóng góp), CỐ Ý không đọc `user_merchant_memory` vì đó là trí nhớ riêng chưa ai đồng ý chia sẻ; phiếu cũ của ocr-service không còn tính vào việc đưa thương hiệu mới vào `kb_brands`, và tên đẹp thay bằng `key.title()`.
- **API HTTP quản trị định dạng** `/api/v1/admin/provider-formats` (`app/api/v1/provider_formats_admin.py`, `app/services/provider_format_admin.py`, `app/schemas/provider_formats.py`): danh sách/chi tiết, tạo draft trống hoặc nhân bản, sửa pattern/trường/mẫu (CHỈ draft; 409 `format_not_draft`), `GET /{id}/check`, `POST /{id}/try`, `activate` (409 `format_not_ready` kèm `details`), `archive`. Khóa nội bộ + `X-Actor-Hash`; mọi chỉnh sửa ghi `kb_audit_log` (chỉ ghi TÊN thứ bị sửa). `RuleError` có thêm `details`. CHƯA có proxy C# của backend cho API này.
- Kiểm chứng: Postgres thật (embedded): `alembic upgrade head`, `alembic check` sạch, hạ về 0026 rồi nâng lại; SQL của ocr-service chạy trên Postgres thật; test ocr-service 618 qua (SQLite giả). ai-service 1544 test qua.
- ERD `docs/diagrams/erd-ai-service-optimized-v5_1.drawio` đã cập nhật (2026-10-26): bỏ `provider_key`/`package_name` khỏi `kb_provider_patterns`; `user_merchant_memory` thêm `display_name` và ghi chú gộp bảng cũ; `kb_audit_log` ghi chú chỉ-ghi-thêm; `ops_analyze_log.pattern_id` thành FK (cạnh mới `SET NULL`); `ml_raw_samples` ghi chú `receipt`; `kb_provider_format_fields` ghi chú CHECK F1-04; bảng mới `bill_unknown_scans` (viền đứt, ngoài ERD có chủ đích); chú giải thêm CHECK hex 64; khung "Cập nhật 2026-10-26". Đã kiểm: không bảng nào chồng nhau, không ghi chú nào bị cắt.
- **Diễn tập trên bản sao DB thật (2026-10-26).** `pg_dump` DB dev (`finmate_ai`, đang ở 0019) ra `backups/finmate_ai-at-0019-*.dump` (đã thêm `backups/` vào `.gitignore`), khôi phục vào Postgres 16 tạm tách biệt, nâng 0019 -> 0029 thành công (mọi CHECK/FK validate trên dữ liệu thật), `alembic check` sạch, số dòng khớp (raw 16+742=758, labeled 742, label events 742, user_merchant_memory 22+61=83, brands 401, keywords 356, patterns 8, formats 5), app khởi động và các endpoint chính 200, SQL của ocr-service chạy được, sinh lại `category-classifier/*.csv` (742 dòng: 542/65/135), toàn bộ test ai-service qua trên Postgres 16. DB dev thật CHƯA bị đụng (vẫn 0019). Còn việc của người vận hành: build lại `ai-service` VÀ `ocr-service` cùng lúc (container ai-service dùng image cũ nên đang lỗi `Can't locate revision 0019`; khi chạy lại nó tự `alembic upgrade head` trên DB thật).
- **Đã nâng DB dev thật (2026-10-26 00:5x).** Sao lưu mới `backups/finmate_ai-before-upgrade-*.dump`, rồi `docker compose up -d --build ai-service ocr-service`: ai-service tự `alembic upgrade head` 0019 -> 0029 thành công, cả hai container healthy. Số dòng khớp lần diễn tập (ml_raw_samples 758, labeled 742, label events 742, user_merchant_memory 83, brands 401, keywords 356, formats 5); `/health`, `/admin/provider-formats`, `/admin/categories`, `/bill/classify`, `/stats` đều 200. Backend C# CHƯA build lại (vẫn image cũ, chưa có AiCategorySyncService).
- Chưa chạy được: Rasa thật (huấn luyện/`rasa test nlu`: chưa cài Rasa, Docker không chạy), sherpa-onnx và mô hình giọng nói thật của stt-service. stt-service 8 test qua với động cơ giả. Chưa làm: proxy C# cho `/admin/provider-formats`.

## Phase 40 — Giọng nói: số tiền lớn bị gộp sai (2026-10-26)
Phát hiện khi chạy 13 file mp3 thật (`stt-service/Data_Voice`) qua stt-service rồi `/chat/parse`.
- **Lỗi 1, `app/utils/number_words.py`:** "hai mươi ba triệu năm trăm tám triệu rưỡi" bị đọc thành MỘT số 531.500.000 (STT không có dấu câu nên hai số dính liền). Đơn vị trong một số phải GIẢM dần (triệu rồi nghìn); đơn vị lặp lại hoặc lớn lên giờ kết thúc số trước, và chữ số cuối cụm ("tám") được trả về cho số kế tiếp (`_give_back_leading_digits`), ra 23,5 triệu và 8,5 triệu. Lỗi cùng họ: "năm mươi nghìn tám mươi nghìn" từng bị cộng thành 130.000.
- **Lỗi 2, `app/services/clauses.py` (`split_on_amounts`):** bộ lọc thể tích loại cả "một lít hai", "ba lít" viết bằng CHỮ (đã qua bộ lọc ngữ cảnh của `find_amounts`), nên "mua sạc một lít hai đi chợ ba lít" mất hai khoản và dồn cả câu vào một dòng. Giờ chỉ loại "N lít" viết bằng chữ số. Hai số tiền bằng chữ đứng liền nhau không còn chữ giữa nay cũng được tách thành hai khoản (`_separates`).
- **KHÔNG phải lỗi:** "mẹ cho tôi hai tỏi" = 2.000.000.000 là đúng theo `amount_slang.json` (tỏi = tỷ, có test "mua nhà ba tỏi hai"). Giả thuyết ban đầu "tỏi bị đọc nhầm thành tỷ" là sai.
- **Ngoài tầm parser:** STT nghe "triệu" thành "chiều", "sang" thành "xăng"; file TTS có câu quảng cáo "giọng đọc được tạo bởi ..." chen vào mô tả.
- Test hồi quy: `test_number_words.py::test_a_unit_that_does_not_shrink_starts_a_new_number` (10 ca), `test_chat_clauses.py` (3 test, câu thật từ file giọng nói). Unit ai-service 1241 qua. Đo trước/sau trên cùng bộ nhãn: `eval_money` words 21/22 thành 22/22, stt_variant 3/5 thành 5/5, các nhóm khác giữ nguyên; `eval_flows --chat` 57/66 thành 65/66. Cần `docker compose up -d --build ai-service` để chạy bản mới (không có migration).

## Phase 41 — Dọn dữ liệu trong AI Service, phiếu bầu món, ocr-service ghi qua API (2026-10-26)
Ba việc theo lựa chọn của người dùng sau phần đánh giá ERD v5_1.
- **Dọn dữ liệu tự động (`app/services/retention.py`).** Vòng nền thứ ba cạnh hai vòng purge đã có: xóa dữ liệu quá hạn của `ops_analyze_log` (số tiền, mô tả, tên cửa hàng), `ml_raw_samples` chưa gắn nhãn (mẫu đã có nhãn và mẫu `seed` được giữ) và `bill_unknown_scans` (theo lần quét cuối), theo lô, `RETENTION_DAYS` (mặc định 90), chạy mỗi `RETENTION_INTERVAL_HOURS`, trễ 300 giây sau khi khởi động; `RETENTION_ENABLED=false` để tắt. Các script cũ vẫn dùng được như lệnh chạy tay (gọi cùng hàm). Test `tests/integration/test_retention.py`.
- **Phiếu bầu món (`ml_item_feedback`, view `v_item_consensus`).** Giữ lại và nối dây: `POST /api/v1/bill/items/feedback` ghi phiếu (mỗi người một phiếu cho một `item_key`, ghi đè khi đổi ý; xóa dữ liệu người dùng cũng xóa phiếu), `scripts/promote_item_consensus.py` đưa món đủ phiếu đồng thuận vào `kb_item_categories` với `source = 'consensus'`, KHÔNG ghi đè dòng đã có (báo các món tranh chấp), ghi `kb_audit_log`. Việc người dùng đã đồng ý chia sẻ do backend gọi chịu trách nhiệm. Test `tests/integration/test_item_votes.py`.
- **ocr-service bỏ kết nối DB.** ai-service thêm API nội bộ `/api/v1/ocr-store/*` (`app/api/v1/ocr_store.py`, `app/services/ocr_store.py`, `app/schemas/ocr_store.py`; Bearer `INTERNAL_API_KEY`): kiến thức và phiếu bầu thương hiệu, bộ nhớ cửa hàng, bill gắn nhãn + bộ chuẩn (gold), bill lạ, lịch sử huấn luyện. ocr-service dùng máy khách `storage/ai_api.py` (httpx) qua `storage_api`, `knowledge_api`, `promotion_api`, `training_api`, `datasets_api`, `config_api`; xóa các module `*_pg`, `pg_fake.py`, bỏ `psycopg`. Cần `AI_SERVICE_URL` (compose đặt) và `AI_SERVICE_API_KEY` (= `INTERNAL_API_KEY`, đặt trong `ocr-service/.env`); thiếu thì chạy chế độ file như cũ. Nhóm bill <-> slug backend vẫn theo `category_groups.json` (`food` đọc lại ra `dining`; slug ngoài 9 nhóm bị bỏ qua); gom thương hiệu đọc `v_merchant_consensus` (chỉ bill đã đồng ý).
- Kiểm chứng: ocr-service 634 test qua (có `test_ai_api.py`: hợp đồng qua `httpx.MockTransport` + chữ ký của AI Service giả khớp máy khách thật); ai-service cả bộ 1588 test qua trên Postgres thật; chạy máy khách `AiApi` thật của ocr-service vào ai-service thật (uvicorn + DB migrate tới 0029): 11 kiểm tra qua (kiến thức, seed lặp, bộ nhớ cửa hàng, từ chối thu nhập, bill gắn nhãn, bill lạ, huấn luyện bị loại và đạt, tài liệu cấu hình, khóa sai bị từ chối).
- **Chạy thật trên `docker compose` (2026-10-26).** Build lại `ai-service` và `ocr-service`, lên healthy. Log ocr-service lúc khởi động: gọi `GET /api/v1/ocr-store/knowledge` và ba tài liệu cấu hình của ai-service, đều 200; container có `httpx`, không có `psycopg`. `POST /merchant-category` (có `X-User-Hash`) vào ocr-service trong container ra 200 và hai dòng xuất hiện trong DB thật: `user_merchant_memory` (slug `shopping`) và `ml_raw_samples` (CHECK `source` đạt). Dòng thử (user hash `e2e0…`) đã xóa; riêng dòng `ml_labeled_samples` đi kèm không kịp xem trước khi xóa (test `test_ocr_store.py` đã phủ).
- **Chưa làm:** backend C# chưa gọi `/bill/items/feedback`; ocr-service cần `AI_SERVICE_API_KEY` trong `.env` ở mọi nơi triển khai (đã thêm vào `.env` cục bộ).

## Phase 42 — Lược đồ DB cho cách quản lý tài chính, khảo sát đầu vào và mục tiêu theo mẫu (2026-10-27)
Chỉ làm **lược đồ** theo thiết kế đã duyệt trong chat (luồng: đăng ký → khảo sát → AI chọn trong danh sách phương pháp của DB → người dùng chọn → gợi ý mục tiêu đi kèm). Migration `20261026202125_AddFinanceMethodsAndOnboarding` (có `Down()` đầy đủ).
- **8 bảng mới.** Danh mục admin chỉnh được: `finance_methods` (`code` bất biến, `param_defs`/`eligibility`/`starter_steps` JSONB, `goal_mode`, `version`, `is_active`), `survey_questions`, `survey_options` (cột `signals` ánh xạ đáp án → signals), `goal_templates`, `method_goal_templates`. Dữ liệu người dùng (FK `users` ON DELETE CASCADE): `user_survey_answers` (lưu theo MÃ chữ, UNIQUE `(user_id, question_code)`), `method_recommendations` (nhật ký gợi ý: signals, nguồn `ai`/`rule_fallback`, `prompt_version`, `method_versions`, `outcome`), `user_methods` (phương pháp đang dùng + phần T1/T2 người dùng ghi đè; UNIQUE từng phần `(user_id, role)` khi `status='active'`).
- **Cột mới trên bảng cũ.** `users.onboarding_status` (`pending`/`completed`/`skipped`) + `onboarding_completed_at`; `saving_goals.goal_template_id` + `user_method_id` (FK `SET NULL`: mất nguồn gốc thì mục tiêu vẫn sống). Migration **backfill** mọi tài khoản đã có thành `completed` (cùng tiền lệ `email_verified_at`, Phase 17) để không đẩy người dùng cũ vào khảo sát.
- **Ràng buộc ở DB.** CHECK enum dạng TEXT, `jsonb_typeof` cho mọi cột JSON, `(outcome='shown') = (decided_at IS NULL)`, cách bổ trợ ≠ cách chính, nguồn `ai` phải có `prompt_version`, `(status='ended') = (ended_at IS NOT NULL)`. Danh mục dùng FK `RESTRICT` (không xóa cứng, tắt bằng `is_active`).
- **Cố ý chưa làm.** `user_method_checkins` (hỏi lại sau 2–4 tuần) và bảng `user_signals` riêng (signals chụp vào `method_recommendations.signals_snapshot`). Dữ liệu gieo (migration không seed): xem Phase 43. Chưa có repository, handler, API người dùng hay API admin, chưa có hằng `AuditEvents` mới.
- **Bốn câu hỏi này đã được quyết cùng ngày — xem Phase 44.** Nguyên văn câu hỏi: (1) T1/T2 chỉnh được mâu thuẫn với `BudgetAlertEvaluator` đang hard-code 70/90/100 và ba cột cờ `budget_periods.alert_70/90/100_sent_at` — migration này KHÔNG đụng chúng; (2) đồng ý gửi signals từ khảo sát cho AI bên thứ ba (`ai_consent_*` hiện chỉ nói về tên món); (3) ai gọi AI (backend hay ai-service) quyết định có cần bảng chỉ dẫn prompt kiểu `bill_ai_instructions` hay không; (4) câu hỏi thu nhập của khảo sát có chồng với `users.monthly_income_cents` không.
- **`.context/ARCHITECTURE.md` chưa sửa** (cần phê duyệt, `AGENTS.md` §4): §4.1 vẫn ghi 25 bảng Backend DB, thực tế nay là 33.
- Kiểm chứng: `dotnet build` 0 cảnh báo / 0 lỗi; `dotnet ef migrations has-pending-model-changes` sạch; `FinanceMethodSchemaTests` 7 test trên Postgres thật (Testcontainers): backfill + Down/Up lại, UNIQUE một chính/một bổ trợ đang hoạt động, các CHECK, JSON sai kiểu, `RESTRICT` của danh mục, cascade khi xóa user, `SET NULL` của mục tiêu; cả bộ **1029 test qua, 0 lỗi, 0 bỏ qua**. Chưa áp migration lên DB dev thật (`finmate_main` chưa chạy trong Docker lúc làm).

## Phase 43 — Seeder: 10 câu khảo sát, 9 cách quản lý, 4 mẫu mục tiêu (2026-10-27)
Gieo dữ liệu cho các bảng của Phase 42, nội dung lấy từ `finmate_khao_sat_insight_va_goi_y_quan_ly_tai_chinh.md` (mục 2 câu hỏi, mục 3 ánh xạ). Ba seeder theo mẫu `MissionSeeder`: thêm theo `code`, chạy lại không đổi gì, **DB luôn thắng** (không ghi đè nội dung, tham số hay `is_active` admin đã sửa). Gọi ở `Program.cs` sau `MascotItemSeeder`; `GoalTemplateSeeder` phải sau `FinanceMethodSeeder`.
- **`SurveySeeder`:** 10 câu × 5 đáp án (50 dòng). Mỗi đáp án ánh xạ sang signals có trọng số 1–3 (cột `survey_options.signals`); câu bỏ qua không có dòng nào nên không tính điểm. Chỉ câu `saving_style` (Q8) có `show_if`: hỏi khi mục tiêu là quỹ dự phòng/mục tiêu cụ thể VÀ người dùng không đang thiếu hụt dòng tiền (file .md mục 6, bước 3). Q5 không điều kiện hóa được theo Q10 như dự tính ban đầu vì Q10 đứng sau — nên người dùng gặp 9–10 câu.
- **`FinanceMethodSeeder`:** 9 cách, mỗi dòng của bảng ánh xạ mục 3: `minimal_tracking`, `category_budget`, `available_to_spend`, `cash_flow_budget`, `envelope_budget`, `goal_fund` (bổ trợ), `emergency_buffer`, `debt_repayment`, `low_effort_automation`. Mỗi cách có `goal_mode`, tham số (T1 70, T2 90 và giờ nhắc 22:00 cho các cách có hạn mức), điều kiện chọn và 3 bước bắt đầu. `eligibility` = `{weights, excludes}`: `weights` cộng điểm, `excludes` loại hẳn khỏi shortlist. Thứ tự ưu tiên của file .md cài bằng `excludes`: người thiếu hụt dòng tiền hoặc trễ nghĩa vụ không bị gợi ý tỷ lệ cứng hay tiết kiệm.
- **`GoalTemplateSeeder`:** 4 mẫu (`starter_emergency_buffer`, `emergency_fund_three_months`, `irregular_expenses_fund`, `specific_target_goal`) và 15 liên kết phương pháp–mẫu. Mẫu không chứa số tiền (khảo sát không hỏi). Phương pháp theo dõi tối giản và trả nợ cố ý không có mẫu nào. Liên kết CHỈ được gieo khi mẫu vừa được tạo — admin gỡ liên kết thì lần khởi động sau không thêm lại; thêm liên kết mới cho mẫu đã có phải qua migration hoặc admin.
- **`FinanceSignals`** (`FinMate.Domain/Entities/FinanceSignals.cs`): 43 signal, mã phương pháp, mã mẫu, kiểu tham số và khóa `t1_pct`/`t2_pct`/`forecast_minute` là hằng số — không dùng chuỗi tự do, vì sai chính tả một signal làm phương pháp không bao giờ khớp mà không có gì báo lỗi.
- **Chưa làm:** bộ chọn phương pháp (tính điểm từ signals, lọc `excludes`, gọi AI, kiểm tra mã trả về), đọc `show_if` khi hiển thị khảo sát, và mọi API/repository/admin cho các bảng này. Văn bản người dùng thấy bằng tiếng Việt, mã bằng tiếng Anh (như `MissionSeeder`). Bốn điểm mở của Phase 42 đã được quyết: xem Phase 44.
- Kiểm chứng: `dotnet build` 0 cảnh báo / 0 lỗi; `FinanceSeedTests` 8 test trên Postgres thật: số lượng, chạy lại không thêm gì, không ghi đè/hồi sinh chỉnh sửa của admin (kể cả liên kết đã gỡ), mọi signal thuộc từ vựng khai báo với trọng số 1–3, `param_defs` hợp lệ (mặc định trong [min, max], T1 < T2), `show_if` chỉ tham chiếu câu đứng trước và đáp án có thật, bất biến an toàn (người thiếu hụt dòng tiền vẫn còn phương pháp chính để gợi ý và không bị gợi ý tiết kiệm). Cả bộ **1037 test qua, 0 lỗi, 0 bỏ qua**.

## Phase 44 — Quyết định bốn điểm mở của Phase 42 (2026-10-27)
Người dùng chọn đúng phương án đề xuất ở cả bốn điểm. Migration `20261026211435_RenameBudgetAlertFlagsAndAddSurveyAiConsent` (có `Down()`).
- **(1) T1/T2 chỉnh được — ĐÃ LÀM.** Cờ cảnh báo đổi tên theo MỨC: `budget_periods.alert_70/90/100_sent_at` thành `alert_t1/t2/over_sent_at` bằng `RenameColumn` (giữ nguyên dữ liệu; test chạy migration lên/xuống trên dữ liệu thật). `BudgetAlertEvaluator.Evaluate` nhận `AlertThresholds`; mức Over luôn là 100%, `BudgetAlert` thêm `Level` và `MarkSent` đóng cờ theo mức chứ không theo phần trăm (T2 có thể trùng số với mức khác). `UpdateBudgetLimitCommandHandler` mở lại cờ qua `BudgetAlertEvaluator.ReopenBelow` thay cho 70/90 cố định. Ngưỡng của người dùng do `IAlertThresholdProvider` (`AlertThresholdProvider`) lấy từ phương pháp đang dùng: cách chính thắng cách bổ trợ; giá trị ghi đè chỉ có hiệu lực khi tham số cho phép sửa và nằm trong [min, max]; cặp vi phạm 1 ≤ T1 < T2 ≤ 99 bị bỏ cả cặp rồi quay về mặc định của phương pháp, rồi về 70/90. `AlertThresholds.FromMethod` không bao giờ ném. `BudgetRepository.GetPeriodsForAlertAsync` lọc thô theo 1% (trước là 70%) vì T1 mỗi người khác nhau. Seeder đổi T2 tối đa 100 → 99. Người dùng chưa có phương pháp nào thì hành vi y hệt trước (70/90/100). **Chưa làm:** API để người dùng ghi đè T1/T2 (chưa có luồng `user_methods`).
- **(2) Đồng ý gửi khảo sát cho AI bên thứ ba — CHỈ SCHEMA.** Đồng ý RIÊNG, tách khỏi `ai_consent_*`: `users.survey_ai_consent_version` + `survey_ai_consent_at` (CHECK: cả hai cùng NULL hoặc cùng có), `ConsentVersions.SurveyAi = 1`, `User.SurveyAiConsentGranted`. Không đồng ý thì gợi ý chạy bằng rule, không gọi AI. **Chưa làm:** cấp/rút qua `POST|DELETE /users/me/consents` (`ConsentCommandHandler`) và hiển thị trong `UserProfileDto`.
- **(3) Gọi AI qua ai-service — CHƯA LÀM.** Backend sẽ gọi ai-service, ai-service gọi mô hình bên ngoài; chỉ dẫn prompt nằm trong AI DB theo mẫu `bill_ai_instructions`. Việc này thuộc ai-service (alembic, quy ước ERD v5_1, route nội bộ) nên làm riêng. Backend chỉ được gửi signals + danh sách phương pháp cho phép, không gửi định danh, và chỉ khi `SurveyAiConsentGranted`.
- **(4) Thu nhập — KHÔNG ĐỔI.** Giữ cả hai: câu 1 cho độ ổn định thu nhập; `users.monthly_income_cents` dùng khi cần số tiền.
- Kiểm chứng: `dotnet build` 0 cảnh báo / 0 lỗi; `has-pending-model-changes` sạch; 30 test mới (`AlertThresholdsTests` đơn vị, `AlertThresholdProviderTests` trên Postgres thật, và hai test migration/ràng buộc trong `FinanceMethodSchemaTests`); các test ngân sách cũ đổi tên cờ và constructor; cả bộ **1067 test qua, 0 lỗi, 0 bỏ qua**. Chưa áp lên DB dev thật. `.context/ARCHITECTURE.md` vẫn chưa sửa (cần phê duyệt).

## Phase 45 — ai-service: chọn cách quản lý tài chính bằng AI (2026-10-27)
Phần ai-service của điểm (3) Phase 44. Hợp đồng đã được người dùng duyệt cùng ngày (hợp đồng như đề xuất, CÓ kiểm vai trò, KHÔNG dùng OpenRouter): `docs/ai-finance-method-recommend-api.md`.
- **Migration `0030_kb_method_prompts`** (có downgrade): bảng `kb_method_prompts(id, prompt_version UQ varchar(50), instruction_text, is_active, created_at, updated_at)` theo mẫu `kb_ai_instructions`: UNIQUE từng phần `WHERE is_active` (đúng MỘT dòng đang dùng), CHECK `prompt_version ~ '^[A-Za-z0-9._-]+$'`, CHECK chỉ dẫn không rỗng. Gieo một dòng `method-select-v1` (`app/services/method_prompt_seed.py`). Đổi chỉ dẫn = thêm dòng với `prompt_version` mới rồi chuyển `is_active` trong một giao dịch (dòng cũ giữ để truy ngược); sửa trong DB, không build lại. Chưa có API quản trị để sửa, chưa có hằng `kb_audit_log` mới.
- **Route `POST /api/v1/finance-methods/recommend`** (`app/api/v1/finance_methods.py`, Bearer `INTERNAL_API_KEY`). Request `{signals:[{key, weight 1–50}], methods:[{code, name, summary, role primary|supporting|both}]}`, `extra = forbid` (thêm `user_id` là 422). Response `{primary:{code, reason}, support:{code, reason}|null, prompt_version, processing_ms}`. Không nhận `user_id`, không ghi DB, không lưu `notification_body`/`amount_cents`; log chỉ có kết quả, mã lỗi cố định và các số đếm (test bắt việc rò signals, mã, lý do, chỉ dẫn vào log).
- **Kiểm đầu ra trước khi trả** (`app/services/method_picker.parse_and_validate`): JSON hỏng, sai cấu trúc (kể cả `support` là mảng), mã ngoài danh sách vừa nhận, bổ trợ trùng chính, sai vai trò, lý do rỗng/quá 240 ký tự/xuống dòng → **502 `ai_invalid_output`**, `error.details = ["<mã>"]`. Không sửa, không cắt, không hỏi lại model khác. Còn: 503 `ai_unavailable` (không có khóa, cầu dao mở, mọi model lỗi), 503 `prompt_missing` (không dòng active), 422 `no_primary_candidate`.
- **Gọi mô hình:** tái dùng `GeminiClient` với client riêng (cầu dao 30 giây tách khỏi hóa đơn và chat): chuỗi model, thử lại một lần ở model cuối khi 429/503, trần cả chuỗi, không cache, KHÔNG qua OpenRouter. Sửa nhỏ `app/bill/gemini.py`: tách `_generate` khỏi `_call` (hành vi cũ giữ nguyên, 139 test Gemini/chat/hóa đơn qua) và thêm `generate_text`. Cấu hình mới `METHOD_GEMINI_MODEL`, `METHOD_GEMINI_FALLBACK_MODELS`, `METHOD_GEMINI_TIMEOUT_SECONDS` (6), `METHOD_GEMINI_OVERALL_SECONDS` (12). Backend nên đặt timeout 15 giây. Chỉ lỗi của nhà cung cấp mới mở cầu dao; đầu ra sai nội dung thì không.
- **Phần BACKEND còn phải làm** (chưa ai làm, ngoài phạm vi ai-service): (a) client gọi route (timeout 15 giây; 401/422/502/503/quá hạn đều → nhánh rule); (b) chỉ gọi khi `SurveyAiConsentGranted`, và chạy bộ lọc `excludes` TRƯỚC khi gửi danh sách; (c) ghi `method_recommendations` (`source = 'ai'`, `prompt_version` và `reasons` lấy từ phản hồi, `signals_snapshot`, `primary_method_id`/`support_method_id` và `method_versions` tự tra từ `finance_methods` vì phản hồi chỉ có `code`); (d) nhánh rule fallback (`source = 'rule_fallback'`, `prompt_version = NULL`) dùng `eligibility.weights`; (e) biến signals đã cộng điểm thành `weight` nguyên 1–50 (giới hạn của hợp đồng).
- **Chưa làm / giới hạn:** `.context/ARCHITECTURE.md` §3.3 và §4.2 chưa ghi route và bảng mới (cần phê duyệt); ERD v5_1 `.drawio` chưa có `kb_method_prompts`; chưa gọi Gemini thật (chỉ `MockTransport`), nên chưa đo chất lượng chọn và độ trễ thật, `METHOD_GEMINI_*` mặc định là giá trị đề xuất; docker compose chưa build lại, DB dev chưa nâng lên 0030 (ai-service tự `alembic upgrade head` khi container chạy lại).
- Kiểm chứng: Postgres 16 thật (nhúng, `pgserver`): `alembic upgrade head` → `check` sạch → downgrade 0029 → upgrade lại → `check` sạch; `test_migration_0030.py` (seed, một dòng active, ràng buộc phiên bản, bộ nạp chỉ dẫn + cache, lùi/nâng, `alembic check`); `test_method_picker.py` (bộ kiểm tra, prompt, schema request) và `test_finance_methods_api.py` (route với mô hình giả và với `GeminiClient` thật trên `MockTransport`: chuỗi model, thử lại, cầu dao, không OpenRouter, log sạch) — 81 test mới. Cả bộ ai-service **1669 test qua, 0 lỗi**.

## Phase 46 — Backend quản lý tài chính: bộ chọn, API người dùng/admin, mục tiêu theo phương pháp (2026-10-27)
Làm hết phần backend còn lại của Phase 42–45 (chọn phương pháp, gọi AI, API, đồng ý, T1/T2, mục tiêu), áp lên DB dev và chạy Gemini thật.
- **Bộ chọn.** `MethodSelector`: điểm = Σ (trọng số phương pháp × trọng số signal của người dùng); bộ lọc `excludes` chạy TRƯỚC; cách bổ trợ phải đạt ≥ 50% điểm cách chính và > 0; đồng điểm theo `sort_order`. Đầu ra AI được **kiểm lại** (`MethodSelector.FromAiPick`): mã phải nằm trong danh sách đã gửi, cách chính không được là `supporting`, bổ trợ không trùng chính, lý do không rỗng — sai thì rơi về rule. Chỉ gọi AI khi `SurveyAiConsentGranted`; `MethodAiClient` timeout 15 giây, mọi lỗi → rule (`rule_fallback`, `prompt_version = NULL`). Gợi ý trong 10 phút với cùng signals được dùng lại (không gọi AI, không ghi thêm dòng).
- **API người dùng:** `GET /survey`, `PUT /survey/answers`, `POST /survey/skip`, `DELETE /survey`; `GET /finance-methods`, `POST /finance-methods/recommendations` (201), `GET .../latest`, `POST .../{id}/skip`; `GET|POST /me/methods`, `PATCH /me/methods/{id}/params` (ghi đè T1/T2, null = xoá ghi đè, ràng buộc 1 ≤ T1 < T2 ≤ 99 kể cả khi chỉ đổi một giá trị), `DELETE /me/methods/{id}`; `GET /me/goal-suggestions`, `GET /me/spendable`; `POST /saving-goals/feasibility`; `POST /saving-goals` nhận `goalTemplateCode`. Đồng ý `surveyAi` qua `POST|DELETE /users/me/consents`, hiện trong `UserProfileDto.SurveyAiConsentGranted`.
- **API admin** (`AdminOnly`, mỗi lần ghi có `audit_logs`, không xoá cứng): `/admin/finance-methods` (+ `/activation`, `/{id}/goal-templates`), `/admin/survey-questions` (+ options, `/activation`), `/admin/goal-templates`. Mã bất biến; sửa nội dung phương pháp tăng `version`.
- **Mục tiêu theo phương pháp:** `GoalPlanningService` + `GoalFeasibilityCalculator` (dùng chung với `GET /saving-goals/{id}/progress`). `goal_mode`: `reserve_first`/`envelope` trừ max(mức cần góp mỗi tháng, đã nạp); `available_to_spend` chỉ trừ số đã nạp; `track_only`/chưa có phương pháp: không trừ. Chưa khai thu nhập → `spendableCents = null` (không phải 0).
- **Chỉnh seed sau khi tính tay ví dụ trong file .md** (trước đó lệch ví dụ): `emergency_buffer` chỉ là `Supporting`; trọng số `cash_flow_budget` và `debt_repayment` chỉnh lại; câu 2D → `occasional_shortfall` (tách khỏi `cash_shortfall` của 2E); câu 8 `show_if not_in ["E"]`; T2 tối đa 99.
- **Kiểm chứng:** cả bộ backend **1172 test qua, 0 lỗi** (trước đó 1067; sau đó thêm validate đầu vào `POST /saving-goals/feasibility` — 3 test — và chạy lại riêng nhóm tài chính + HTTP: 26 qua, chưa chạy lại cả bộ). Mới: 120 test đơn vị phần tài chính/ngưỡng, test seed + chọn trên Postgres thật (khớp ví dụ .md), 9 test HTTP cả luồng + quyền admin + cô lập người dùng. DB dev: đã rebuild `backend` + `ai-service`; migration EF lên `20261026211435`, 10 câu / 50 đáp án / 9 phương pháp / 4 mẫu mục tiêu / 15 liên kết; alembic ai `0030` và `method-select-v1` đang active. Smoke test dev: đăng ký → khảo sát → gợi ý (rule) → đồng ý `surveyAi` → gợi ý `source = 'ai'`, `prompt_version = 'method-select-v1'` đã lưu.
- **Gemini thật (tín hiệu tổng hợp, không dữ liệu người dùng), 8 hồ sơ:** với mặc định `METHOD_GEMINI_TIMEOUT_SECONDS=6`, `OVERALL=12` chỉ **5/8 thành công**, độ trễ 5,9–8,6 giây; mô hình chính `gemini-3-flash-preview` thường quá 6 giây nên rơi sang `gemini-3.1-flash-lite`; một lượt quá 12 giây mở cầu dao 30 giây làm hai lượt sau 503. Với 14/20 giây: **8/8 thành công, 8/8 chọn cách chính hợp lý**, trung vị 8,4 giây, tối đa 16,5 giây. Qua backend trên dev: 12 giây. **Đã đổi mặc định theo yêu cầu: `METHOD_GEMINI_TIMEOUT_SECONDS=10`, `METHOD_GEMINI_OVERALL_SECONDS=14`** (`ai-service/app/core/config.py`, `docs/ai-finance-method-recommend-api.md`), đã rebuild `ai-service`. Đo lại: lượt đầu 8/8 (trung vị 6,7 s, tối đa 12,7 s); hai lượt liền sau mỗi lượt có một lần chạm trần 14 s rồi cầu dao mở làm 7 lần còn lại 503 tức thì; chạy giãn cách 20 giây/lần: **8/8 thành công, 8/8 hợp lý, trung vị 3,7 s, tối đa 10,9 s**. Nhật ký cho thấy `gemini-3-flash-preview` trả **429 Too Many Requests ở 7/8 lượt giãn cách** nên gần như mọi lượt do `gemini-3.1-flash-lite` trả lời (nhanh, ~3 s) — lần đo 6,5–16 s trước đó là do 429 + thử lại + model dự phòng chứ không hẳn do model chính chậm; các lượt đo dồn dập của chính tôi cũng góp vào hạn mức. **Còn mở:** có nên đặt `METHOD_GEMINI_MODEL=gemini-3.1-flash-lite` làm model chính cho route này (như `chat_gemini_model`)? Đã đo 24 lượt thành công trên lite, chất lượng chọn không kém.
- **Giới hạn / chưa làm:** ERD v5_1 (AI DB) chưa có `kb_method_prompts`; ERD backend mới ở `docs/diagrams/erd-backend-finance-methods.drawio` (chưa mở bằng draw.io); chưa có API sửa `kb_method_prompts`; chưa test tải. Tài khoản `smoke…@finmate.local` tạo ở DB dev khi kiểm thử còn đó. Chưa commit.

## Phase 47 — OpenRouter làm dự phòng thứ hai cho route chọn cách quản lý tài chính (2026-10-27)
Người dùng hỏi có dùng được model free của OpenRouter không và chọn "hướng 2" (thêm dự phòng thứ hai, đảo quyết định "không dùng OpenRouter" của Phase 45). Hợp đồng đã sửa trong `docs/ai-finance-method-recommend-api.md` (mục "Dự phòng thứ hai", điểm cần duyệt 3) và `.context/ARCHITECTURE.md` §3.8.
- **ai-service:** `app/services/method_backup.py` (`OpenRouterPicker`): chỉ chạy khi chuỗi Gemini hết cách, thử lần lượt `METHOD_OPENROUTER_MODELS`, mỗi lần gửi `provider.data_collection = "deny"` (hằng trong code, không cấu hình được), `reasoning.enabled = false`, `response_format` json_schema strict; ngân sách thời gian chung `METHOD_TOTAL_SECONDS=24` (không bắt đầu lần gọi mới khi còn < 2 giây), cầu dao riêng 30 giây, mã lỗi cố định, không log nội dung. Đầu ra vẫn qua `parse_and_validate` (không nới). `build_schema`/`build_backup_schema` giờ giới hạn enum theo VAI TRÒ (cách chính chỉ trong primary/both, bổ trợ trong supporting/both) — áp dụng cho cả Gemini. `details` của 503 có thêm phần tử thứ hai (lý do dự phòng). Response KHÔNG đổi (backend không phân biệt nguồn); log có `provider`.
- **Backend:** `ConsentVersions.SurveyAi` 1 → 2 (người đã đồng ý bản 1 không còn `SurveyAiConsentGranted`, phải đồng ý lại; trong lúc đó chạy bằng rule); timeout `MethodAiClient` 15 → 30 giây.
- **Đo thật (OpenRouter, `data_collection: deny`, 4 hồ sơ × 5 model):** `nemotron-3.5-super-vl:free` 404 (không endpoint theo chính sách dữ liệu — model free đều "Free model training"); `gemma-4-26b/31b:free` và `xing4.0-29b:free` 429; **`dots-studio/dots-3-note-preview:free` 4/4 đúng nhưng 23-28 giây** do suy luận; tắt suy luận (`enabled:false`) còn 3-4 giây. Model mặc định cũ trong config (`nemotron-3-super-120b:free`) không còn trong danh sách OpenRouter.
- **Đo qua route (Gemini bị tắt để ép sang OpenRouter):** lần 1 (schema chưa theo vai trò) 6/8, 5/6 hợp lý, 2/8 `wrong_role` bị bộ kiểm loại; sau khi enum theo vai trò: 3 lượt đầu đúng cả 3 (3,7-5,3 s), lượt thứ 4 quá 10 giây (`backup_timeout`) mở cầu dao 30 giây nên 4 lượt sau 503 tức thì (do gọi dồn dập). Gemini (đường chính, giãn cách) sau khi đổi schema: 8/8, 8/8 hợp lý, trung vị 4,4 s.
- **Giới hạn:** chỉ MỘT model free dùng được với `deny`, nên dự phòng này mong manh (một lần quá hạn là cầu dao 30 giây); rule vẫn là đường lui cuối. Mẫu đo nhỏ (4-8 lượt). **Câu chữ đồng ý hiển thị cho người dùng (app) phải nêu OpenRouter** — backend chỉ giữ số phiên bản, chưa có chỗ nào trong repo này chứa câu chữ. `ConsentVersions.Ai`/`Terms` (tên món) KHÔNG đổi.
- Kiểm chứng: ai-service `tests/unit` 1321 qua, 1 bỏ qua (thêm 9 test dự phòng + 3 test schema/prompt); backend nhóm tài chính + đồng ý 169 qua (thêm 1 test bản đồng ý cũ → chạy rule). Đã rebuild `ai-service` và `backend` trên dev. Cả bộ backend chạy lại sau thay đổi: **1176 qua, 0 lỗi, 0 bỏ qua** (9 phút 7 giây). Chưa commit.

## Phase 48 — Nội dung thông báo tách bằng nhãn và phân loại bằng AI khi từ điển không chắc (2026-10-30)
Thông báo kiểu "TK 1234 GD: -1,000,000VND luc … SD: … ND: mua quan ao" nay có `description` = "mua quan ao" (trước đây là cả đoạn có số dư/số tài khoản) và, khi từ điển không biết, được phân loại bằng Gemini.
- **Nhãn "nội dung" ở DB.** Tài liệu cấu hình `narrative_labels` (`config_documents`; mặc định `nd`, `noi dung`, `ndung`, `content`; `app/data/narrative_labels.json` chỉ là seed, DB thắng, seed lỗi không làm sập khởi động). Chỉ nhãn đứng đầu một mệnh đề (đầu chuỗi hoặc sau `.`/`|`/`;`) mới tính, nên "NGUYEN VAN ND chuyen tien" không bị cắt. Không có nhãn thì `description` giữ cả đoạn như cũ nhưng AI **không** được gọi.
- **Phân loại bằng AI** (`app/pipeline/stages/ai_categorizer.py`, `categorize_with_ai`). Gọi khi: luật ra `other` do không khớp từ điển, giao dịch chi, `allow_ai=true`, nội dung do nhãn/trường mô tả của định dạng tách ra, không rỗng sau khi bỏ số tiền, không phải sức khỏe. Chỉ gửi đúng một cụm nội dung đã che số (dãy ≥ 4 chữ số, số tiền); không gửi số tiền, số dư, số tài khoản. Gemini qua `get_chat_gemini_client` (không OpenRouter), trần `NOTIFICATION_AI_TIMEOUT_SECONDS` = 8 giây; lỗi/quá hạn/`unknown`/`income` → giữ `other`, `/analyze` không lỗi vì AI. Độ tin cậy `categorizer.ai` = 0,70 (< 0,85 nên không một chạm). Không ghi vào bảng nhớ dùng chung.
- **Cờ `allow_ai`.** Trường tùy chọn mặc định `false` trên `/analyze` (hai phía, tương thích ngược; response không đổi). Backend gửi `true` chỉ khi người dùng đã đồng ý AI; `RetryFailedNotificationJob` dựng request không có đồng ý nên gửi `false`.
- **File chính:** ai-service `app/pipeline/stages/{ai_categorizer,extractor}.py`, `app/pipeline/orchestrator.py`, `app/data/{narrative_labels.json,confidence.json}`, `app/core/config.py`; test `tests/unit/test_notification_ai_category.py`, `tests/unit/test_narrative_labels_document.py`. Backend: `AnalyzeRequest`/client gọi ai-service và luồng nhận thông báo.
- Kiểm chứng: ai-service `tests/unit` **1392 qua**; `tests/integration` **367 qua** (Postgres thật); backend `dotnet build` 0 cảnh báo, `dotnet test` **1184 qua, 0 lỗi**. Chạy tay không gọi AI ngoài (Gemini giả): "ND: mua quan ao" → description "mua quan ao", luật `other` 0,35; `allow_ai=false` giữ `other`, `allow_ai=true` → `shopping` 0,70, Gemini giả chỉ nhận "mua quan ao". "Thanh toan ca phe" → `food` 0,9 từ điển, AI không được gọi.
- **Chưa kiểm chứng:** Gemini thật (chất lượng phân loại, độ trễ, ngưỡng 8 giây); thiết bị thật; đường nhập tay và job thử lại luôn gửi `allow_ai=false` nên chưa dùng AI; TÊN NGƯỜI trong phần nội dung ("chuyen tien cho nguyen van a") vẫn có thể tới Gemini khi người dùng đã đồng ý AI (không lọc được bằng quy tắc).
- **Đề xuất cho chủ dự án (chưa sửa `ARCHITECTURE.md`):** thêm trường `allow_ai` (bool, mặc định false) vào hợp đồng `/analyze` ở §3.3.

## Phase 49 — Cấu hình hệ thống, key bên thứ ba và quản trị định dạng thông báo từ trang admin (2026-10-30)
Người dùng muốn admin web sửa được: định dạng thông báo ngân hàng, cấu hình hệ thống có lịch sử + hoàn tác (bảo trì kèm popup trên app, phiên bản app, tính năng, giao diện, logo) và key bên thứ ba (Gemini) lưu an toàn. Làm bốn giai đoạn, mỗi giai đoạn một commit.
- **Đường chuyển tiếp admin (không đổi schema).** `/admin/provider-formats` (đủ 17 route của ai-service: danh sách, chi tiết, kiểm tra, thử, nhân bản, pattern/trường/mẫu, kích hoạt, lưu trữ; ghi → một dòng `audit_logs` KHÔNG kèm thân yêu cầu) và `/admin/rasa/status|parse` (chỉ đọc; Rasa tắt trả `available:false`). Huấn luyện Rasa không có API vì đi qua `scripts/nlu_model.py` có cổng đo. Dùng lại `IAiRulesAdminClient` (thêm area `provider-formats`, `rasa`, `secrets`).
- **Cấu hình hệ thống có phiên bản (BẢNG MỚI `system_config_versions`, migration `AddSystemConfigVersions`).** Lịch sử tuyến tính bất biến (lưu hay hoàn tác đều tạo dòng MỚI; chỉ mục duy nhất một dòng `active`/tên), khóa lạc quan `expectedVersion` (409 `ADMIN_SYSTEM_CONFIG_STALE`), kiểm tra bằng FluentValidation (màu HEX 6 số, phiên bản số.số.số và min ≤ latest, ngưỡng near < over, font chỉ chữ/số/cách, logo chỉ https, cỡ chữ 0,8–1,5). `/admin/system-config` (get, PUT, versions, rollback); nhật ký chỉ ghi TÊN các phần đã đổi. `ISystemConfigProvider` cache trong bộ nhớ 10 giây (cố ý không dùng Redis: middleware hỏi mọi request). `MaintenanceMiddleware` (sau `UseAuthentication`): bảo trì bật → 503 `MAINTENANCE` trừ `/api/v1/app-config`, `/api/v1/auth/*`, `/health`, `/hangfire`, `/swagger`; Admin vẫn qua.
- **NGOẠI LỆ cần chủ dự án xác nhận:** `GET /api/v1/app-config` ẩn danh CÓ CHỦ Ý (màn đăng nhập cũng phải hiện popup bảo trì), chỉ đọc, dữ liệu không bí mật, ETag + `Cache-Control` 30 giây, vẫn qua giới hạn tần suất theo IP. AGENTS.md §3.2 chỉ cho phép ẩn danh ở `/auth/*`; tiền lệ `/health`. Chưa sửa `ARCHITECTURE.md` (thêm endpoint này vào hợp đồng là việc của chủ dự án).
- **Kho key mã hóa (BẢNG MỚI `secret_store`, ai-service migration `0031`).** AES-256-GCM, nonce 12 byte mỗi lần lưu, tên key làm AAD; khóa chủ ở `SECRETS_MASTER_KEY` (không ở DB; thiếu thì từ chối lưu 422, key ở biến môi trường vẫn chạy). Không API nào trả key: chỉ nguồn (`db`/`env`/`none`), 4 ký tự cuối, lúc đổi; có "thử kết nối" (header, không đặt key vào URL). `GeminiClient` và `OpenRouterPicker` nhận hàm lấy key nên đổi key áp dụng ngay (bản chạy khác trong ≤30 giây qua vòng `config_runtime`); biến môi trường là dự phòng ("DB thắng"). Nhật ký đổi key ở `kb_audit_log` (`resource='secret'`, chỉ tên/4 ký tự cuối/lý do): bảng này CHỈ THÊM (trigger chặn DELETE) nên migration 0031 mở rộng `chk_kb_audit_log_resource` thêm `secret` (như 0026 làm cho `category`), bước lùi trả ràng buộc cũ ở dạng NOT VALID. Key của backend (Brevo, Google, FCM) CHƯA nằm ở kho này: chúng dựng một lần lúc khởi động từ biến môi trường.
- **Sửa nhánh đọc thông báo tìm ra khi làm (xem Phase 48):** nhãn số dư chỉ áp cho con số liền sau nó; từ vựng thu/chi, marker số dư và marker phân loại chuyển thành tài liệu cấu hình `notification_vocab` ở DB; bộ khớp pattern chịu được dấu (thử văn bản gốc trước, rồi bản bỏ dấu, trả nhóm về chữ gốc).
- **Admin web (`FinMate_Admin`, không phải git repo; sao lưu ở `FinMate_Admin_backup_2026-10-30`).** Cấu hình tách "Cấu hình hệ thống" và "Cấu hình AI" (Từ điển/Luật/Kiến thức/Rasa), tài liệu gom theo trường `group` do ai-service trả; tab Định dạng thông báo, Rasa, Cấu hình hệ thống (bảo trì, phiên bản app, tính năng, ngưỡng, đồng bộ, giao diện kèm xem trước, lịch sử + khôi phục), Tích hợp bên thứ ba (key che, thử kết nối, đổi, xóa).
- **App Flutter (`FinMate_FE`, nhánh `develop`, CHƯA commit).** `lib/core/config/*`: tải `/app-config` (bản đệm file JSON, hỏi lại mỗi 60 giây và khi app quay lại; mất mạng giữ bản cũ nên không tự bật bảo trì), `AppConfigGate` chặn bằng popup bảo trì (thử lại có trạng thái bận) hoặc màn buộc cập nhật (liên kết cửa hàng dạng chữ + nút sao chép vì chưa có `url_launcher`), `AppTheme.applyConfig` (màu chủ đạo, nền, thanh điều hướng, font Google), hệ số cỡ chữ nhân với cỡ chữ của hệ điều hành, `BrandLogo` (https, lỗi thì về mặc định). Không thêm dependency; `kAppVersion` là hằng số khớp `pubspec.yaml` (có test).
- Kiểm chứng: ai-service **1841 qua**; backend `dotnet test` **1227 qua, 0 lỗi**; FE `flutter analyze` sạch, **28 test mới đạt**; admin `tsc`, `eslint`, `vite build` đạt, giao diện thử trên máy chủ giả.
- **Chưa kiểm chứng / giới hạn:** thiết bị thật (popup, theme, logo); "thử kết nối" Gemini/OpenRouter thật; admin chưa chạy `test:browser` với backend thật. Theme chỉ đổi phần do `ThemeData` điều khiển: khoảng 100 chỗ dùng thẳng hằng `AppColors.*` vẫn giữ màu mặc định cho tới khi chuyển sang đọc từ theme. Công tắc tính năng (`features`) đã tải về nhưng CHƯA nối vào màn hình nào. FE có 8 test `test/integration/app_flow_test.dart` đỏ SẴN TỪ TRƯỚC (không tìm thấy `Checkbox` ở `loginFromWelcome`), chưa tìm nguyên nhân.
