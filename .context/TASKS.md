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

- [-] Đăng ký bằng SĐT + OTP — *Skipped: user chốt chưa cần trong MVP (2026-09-11). Giữ Email/Password + Google login. Docx bước 1.1 coi như overspec ở điểm này.*

### Quyết định #4 — Voice input + Receipt OCR (kéo từ Backlog lên MVP)

- [ ] `TransactionSource` thêm `Voice` và `Receipt` — *phân biệt kênh nhập để phân tích sau*
- [ ] Voice: STT chạy ở client (Android), text đẩy vào `POST /transactions/parse` sẵn có — *không cần route AI Service mới*
- [ ] OCR: thêm route `POST /api/v1/ocr` vào contract Backend↔AI Service (`ARCHITECTURE.md` §3.3) — *[!] đổi contract cần user duyệt trước, AGENTS.md §5*
- [ ] OCR: endpoint backend nhận ảnh hóa đơn → trả field trích xuất để client prefill
- [ ] Tests cho cả 2 kênh

---

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
| Phase 10 — Đối chiếu Core User Flows | `[~]` | 16 / 22 *(#1 và #2 xong; #4 Voice/OCR chờ Phase 9; 1 task `[-]` Skipped: OTP)* |
| **Total** | | **253 / 261** |

**Còn lại:** 5 task chưa làm (Phase 10 quyết định #4 — Voice + Receipt OCR)
+ 1 task `[!]` chờ duyệt dependency FCM + 2 task `[-]` bỏ có chủ ý (OTP, bảng `ab_*`).

---

**Verify Phase 2 (2026-09-10):** `dotnet build` sạch 0 warning; `dotnet test` xanh 20/20 (chạy qua container SDK 9.0); `dotnet ef migrations has-pending-model-changes` sạch; `docker compose up` full stack từ volume rỗng — migration áp dụng sạch, seed đủ 5 provider configs; smoke test curl end-to-end (register/login → tạo bank account → tạo trùng package_name [409] → tạo cash account → list → toggle monitoring → get balance → delete → get balance sau xóa [404]) đều đúng như thiết kế. Một bug hạ tầng test được tìm thấy và sửa: `AuthApiFactory` set config qua `Environment.SetEnvironmentVariable` (process-wide) — khi 2 test class dùng factory riêng chạy song song (xunit mặc định chạy khác class song song), race trên biến môi trường khiến 2 `WebApplicationFactory` đôi khi trỏ cùng lúc vào 1 Postgres container, gây deadlock/connection-refused ngẫu nhiên. Fix bằng cách gom mọi integration test class vào chung 1 `[Collection("Integration")]` để chạy tuần tự.

**Verify Phase 3+4 (2026-09-10):** `dotnet build` sạch 0 warning; `dotnet test` xanh 94/94, chạy lặp lại 2 lần liên tiếp không flake (chạy qua container SDK 9.0); `dotnet ef migrations has-pending-model-changes` sạch; `docker compose up` full stack (bao gồm `ai-service` thật — vẫn chỉ là FastAPI scaffold trống của Phase 0, chưa có route `/api/v1/analyze`) — smoke test curl end-to-end: seed đủ 11 category; tạo cash account → tạo manual transaction (debit) → balance giảm đúng → xóa account khi còn giao dịch bị 409 → xóa transaction → balance khôi phục → xóa account thành công; gọi `/notifications/analyze` với package đã whitelist nhắm vào `ai-service` thật (chưa có route) → xác nhận trả đúng `503 NOTIFICATION_AI_SERVICE_UNAVAILABLE` (không crash 500), `notification_logs.status='failed'` đúng trong DB, log không có exception chưa xử lý. Nhánh AI thành công (financial → tạo transaction draft → confirm) được cover đầy đủ qua integration test với `FakeAIServiceClient` (chưa thể verify qua curl thật vì Phase 9 chưa code AI Service) — cùng giới hạn đã ghi nhận ở Google login.

**Verify Phase 5 (2026-09-11):** `dotnet build` sạch 0 warning; `dotnet test` xanh 153/153 (chạy qua container SDK 9.0); `dotnet ef migrations has-pending-model-changes` sạch; `docker compose up --build` full stack từ volume rỗng — migration áp dụng sạch, 4 recurring job đăng ký đủ (`budget-alerts`, `goal-deadline-check`, `data-deletion`, `retry-failed-notifications`), log không có exception chưa xử lý (ngoài 1 lỗi `__EFMigrationsHistory` lúc khởi động DB rỗng, EF tự xử lý, đã có từ các phase trước). Smoke test curl end-to-end: seed đủ 11 category → tạo budget food + budget tổng → tạo budget food lần 2 nhận 409 → chi 250k vào food thì cả 2 budget cùng lên 250k → đổi giao dịch sang transport thì food về 0 còn budget tổng giữ 250k → ghi nhận thu nhập 15tr không tiêu hạn mức nào → xóa giao dịch thì mọi budget về 0 → nâng hạn mức food lên 2tr, `%` tính lại đúng → tạo mục tiêu 10tr, góp 3tr (30%, on-track, cần 77.778đ/ngày) → góp nốt 7tr thì status thành `completed` → góp tiếp nhận 422 → **số dư tài khoản không đổi vì đóng góp mục tiêu là bookkeeping thuần** (10tr ban đầu + 15tr thu nhập = 25tr).

Hai lỗi tìm được khi chạy thật (không lộ ra ở unit test) và đã sửa: (1) Npgsql từ chối `DateTimeOffset` offset +07:00 cho cột `timestamptz` khiến mọi endpoint budget trả 500 — mốc chu kỳ giờ trả về ở UTC, xem note biên chu kỳ ở trên; (2) response summary có field `totalSpentCents` = 0 nằm ngay trên dòng budget tổng đang hiện 250k (vì nó chỉ cộng budget theo category) — đã tách hẳn `TotalBudget` khỏi `CategoryBudgets` để không còn con số nào đọc nhầm được.

**Verify Phase 6+7 (2026-09-11):** `dotnet build` sạch 0 warning; `dotnet test` xanh 281/281 (chạy qua container SDK 9.0); `dotnet ef migrations has-pending-model-changes` sạch; `docker compose up --build` full stack từ volume rỗng — migration sạch, seed đủ 7 mission + 8 mascot item, **8 recurring job** đăng ký đủ (`daily-summary`, `insight-generator`, `streak-check`, `mission-reset` + 4 job cũ), log không có exception chưa xử lý (ngoài 1 lỗi `__EFMigrationsHistory` lúc khởi động DB rỗng, EF tự xử lý, đã có từ các phase trước).

Smoke test curl end-to-end: tạo giao dịch trải nhiều ngày → `monthly-summary` khớp `category-breakdown` và `timeline` (900k / 5 giao dịch, phần trăm tổng đúng 100) → `forecast` giữ nguyên phần đã tiêu và báo `confidence: low` khi dữ liệu mỏng → profile khởi tạo lazily, giao dịch thủ công cộng EXP và hoàn thành `daily_manual_1` → đóng góp mục tiêu nhảy 2 bậc lên level 3 và mở khóa 3 mascot item (kèm `celebration` trong response) → mặc mũ OK (200), mặc 2 mũ cùng loại bị chặn (422) → xóa giao dịch trừ lại đúng 10 EXP.

**Một lỗi tìm được khi chạy thật (không lộ ra ở unit test) và đã sửa:** `GetMonthlySummaryQuery` chỉ cộng `daily_summaries` cho các ngày trước hôm nay, nên trên môi trường vừa dựng (và bất kỳ ngày nào `DailySummaryJob` lỗi) báo cáo **âm thầm thiếu dữ liệu** — smoke test cho tổng tháng 300k trong khi breakdown và timeline cùng ra 900k. Không thể suy "thiếu dòng summary" thành "ngày đó không tiêu gì". Đã sửa: đọc summary có sẵn rồi lấp mọi ngày chúng chưa phủ bằng dữ liệu live, khớp theo ngày nên ngày đã chốt không bị cộng hai lần; đổi lại là một truy vấn có giới hạn (tối đa 31 ngày của 1 user, đi qua index `(user_id, transacted_at, id)`).

*Last updated: 2026-09-11*
*Next priority: Phase 10 quyết định #4 (Voice + Receipt OCR, 5 task) — Voice đã hết bị chặn sau Phase 9; OCR cần user duyệt route mới trong contract Backend↔AI Service theo AGENTS.md §5.*

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
