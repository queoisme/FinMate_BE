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
- [!] Unit: không xóa account đang có transactions — *Blocked by Phase 4: phụ thuộc `Transaction`/`ITransactionRepository`, chưa tồn tại ở Phase 2. `DeleteFinancialAccountCommandHandler` hiện chỉ soft-delete đơn thuần; sẽ bổ sung guard + test khi Phase 4 dựng `ITransactionRepository`.*

---

## Phase 3 — Domain 6: Categories

### Database

- [ ] Migration: tạo bảng `categories`
- [ ] Seed: 11 system categories (food, transport, shopping, education, housing, bills, entertainment, health, family, income, other)

### Backend

- [ ] Entity: `Category`
- [ ] Repository: `ICategoryRepository`
- [ ] Query: `GetCategoryListQuery` (system + user custom)
- [ ] Command: `CreateCategoryCommand` + Handler + Validator
- [ ] Command: `UpdateCategoryCommand` + Handler
- [ ] Command: `DeleteCategoryCommand` + Handler (check có transaction không)
- [ ] Controller: `CategoriesController`

---

## Phase 4 — Domain 4 & 5: Notifications & Transactions (Core)

### Database

- [ ] Migration: tạo bảng `notification_logs`
- [ ] Migration: tạo bảng `ai_results`
- [ ] Migration: tạo bảng `saving_goals` (trước transactions vì FK)
- [ ] Migration: tạo bảng `transactions`

### Backend — Notification Module

- [ ] Entity: `NotificationLog`, `AiResult`
- [ ] Repository: `INotificationLogRepository`
- [ ] Service: `IAIServiceClient` interface + `AIServiceClient` implementation (Refit)
- [ ] Command: `AnalyzeNotificationCommand` + Handler
  - [ ] Validate package trong whitelist user
  - [ ] Hash dedup check
  - [ ] Gọi AI Service
  - [ ] Lưu notification_log và ai_result
  - [ ] Tạo transaction draft nếu financial
  - [ ] Push notification về Android
- [ ] Controller: `NotificationsController`
- [ ] Job: `RetryFailedNotificationJob` (retry status='failed', retry_count < 3)

### Backend — Transaction Module

- [ ] Entity: `Transaction`
- [ ] Repository: `ITransactionRepository`
- [ ] Command: `ConfirmTransactionCommand` + Handler
  - [ ] Validate ownership
  - [ ] DB transaction cho cascade: budget + gamification + streak
  - [ ] Budget period update
  - [ ] EXP award
  - [ ] Streak check
  - [ ] Mission condition trigger
- [ ] Command: `CreateManualTransactionCommand` + Handler + Validator
- [ ] Command: `UpdateTransactionCommand` + Handler + Validator
  - [ ] Revert budget nếu category/amount/date thay đổi
  - [ ] Lưu AI correction nếu category thay đổi
- [ ] Command: `DeleteTransactionCommand` + Handler
  - [ ] Revert budget nếu đã confirmed
  - [ ] Revert EXP
- [ ] Command: `ParseNaturalLanguageCommand` + Handler (gọi AI Service)
- [ ] Query: `GetTransactionListQuery` + Handler (filter, cursor pagination)
- [ ] Query: `GetTransactionDetailQuery` + Handler
- [ ] Controller: `TransactionsController`

### Tests — Critical

- [ ] Unit: `ConfirmTransactionCommandHandler`
  - [ ] Cascade budget update
  - [ ] EXP award
  - [ ] Streak update
  - [ ] Rollback khi lỗi
- [ ] Unit: `DeleteTransactionCommandHandler` — revert logic
- [ ] Unit: `UpdateTransactionCommandHandler` — category change revert
- [ ] Integration: Full notification → draft → confirm flow

---

## Phase 5 — Domain 7 & 8: Budget & Saving Goals

### Database

- [ ] Migration: tạo bảng `budgets`
- [ ] Migration: tạo bảng `budget_periods`
- [ ] Migration: tạo bảng `goal_contributions`

### Backend — Budget Module

- [ ] Entity: `Budget`, `BudgetPeriod`
- [ ] Repository: `IBudgetRepository`
- [ ] Service: `IBudgetPeriodService` (incremental update logic)
- [ ] Command: `CreateBudgetCommand` + Handler + Validator
- [ ] Command: `UpdateBudgetLimitCommand` + Handler
- [ ] Command: `DeleteBudgetCommand` + Handler
- [ ] Query: `GetBudgetSummaryQuery` (current month overview)
- [ ] Controller: `BudgetsController`
- [ ] Job: `BudgetAlertJob`
  - [ ] Query budget_periods gần limit
  - [ ] Check `alert_80_sent_at` và `alert_100_sent_at`
  - [ ] Gửi push notification
  - [ ] Update sent timestamps

### Backend — Saving Goals Module

- [ ] Entity: `SavingGoal`, `GoalContribution`
- [ ] Repository: `ISavingGoalRepository`
- [ ] Command: `CreateSavingGoalCommand` + Handler + Validator
- [ ] Command: `UpdateSavingGoalCommand` + Handler
- [ ] Command: `ContributeToGoalCommand` + Handler
  - [ ] Cộng vào saved_cents
  - [ ] Auto-complete nếu đạt target
  - [ ] Trigger Mascot celebration
- [ ] Command: `CancelSavingGoalCommand` + Handler
- [ ] Query: `GetSavingGoalListQuery`
- [ ] Query: `GetGoalProgressQuery` (on-track calculation)
- [ ] Controller: `SavingGoalsController`
- [ ] Job: `GoalDeadlineCheckJob` — check goals quá deadline

### Tests

- [ ] Unit: Budget alert threshold logic
- [ ] Unit: Goal auto-complete khi đạt target
- [ ] Unit: On-track calculation

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

- [ ] OAuth2 social login (Google)
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
| Phase 1 — Auth & Profile | `[x]` | 24 / 24 |
| Phase 2 — Financial Accounts | `[x]` | 10 / 11 *(+1 sub-task blocked: check has-transactions, phụ thuộc Phase 4)* |
| Phase 3 — Categories | `[ ]` | 0 / 8 |
| Phase 4 — Notifications & Transactions | `[ ]` | 0 / 28 |
| Phase 5 — Budget & Goals | `[ ]` | 0 / 22 |
| Phase 6 — Reports | `[ ]` | 0 / 12 |
| Phase 7 — Gamification | `[ ]` | 0 / 20 |
| Phase 8 — Admin | `[ ]` | 0 / 12 |
| Phase 9 — AI Service | `[ ]` | 0 / 24 |
| **Total** | | **49 / 176** |

---

**Verify Phase 2 (2026-09-10):** `dotnet build` sạch 0 warning; `dotnet test` xanh 20/20 (chạy qua container SDK 9.0); `dotnet ef migrations has-pending-model-changes` sạch; `docker compose up` full stack từ volume rỗng — migration áp dụng sạch, seed đủ 5 provider configs; smoke test curl end-to-end (register/login → tạo bank account → tạo trùng package_name [409] → tạo cash account → list → toggle monitoring → get balance → delete → get balance sau xóa [404]) đều đúng như thiết kế. Một bug hạ tầng test được tìm thấy và sửa: `AuthApiFactory` set config qua `Environment.SetEnvironmentVariable` (process-wide) — khi 2 test class dùng factory riêng chạy song song (xunit mặc định chạy khác class song song), race trên biến môi trường khiến 2 `WebApplicationFactory` đôi khi trỏ cùng lúc vào 1 Postgres container, gây deadlock/connection-refused ngẫu nhiên. Fix bằng cách gom mọi integration test class vào chung 1 `[Collection("Integration")]` để chạy tuần tự.

*Last updated: 2026-09-10*
*Next priority: Phase 3 — Domain 6: Categories*
