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

- [ ] Tạo `docker-compose.yml` với đủ services (backend, ai-service, postgres-main, postgres-ai, redis)
- [ ] Tạo `.env.example` cho backend và ai-service
- [ ] Cấu hình Serilog structured JSON logging cho backend
- [ ] Cấu hình structlog cho ai-service
- [ ] Setup `EditorConfig` và `.gitignore`

### Backend Project Init

- [ ] Tạo solution `FinMate.sln` với 4 projects (API, Application, Domain, Infrastructure, Tests)
- [ ] Cấu hình `nullable enable`, `implicit usings` toàn solution
- [ ] Cấu hình Npgsql với `UseSnakeCaseNamingConvention()`
- [ ] Cấu hình `ExceptionHandlingMiddleware`
- [ ] Cấu hình FluentValidation auto-registration
- [ ] Cấu hình AutoMapper profile scan
- [ ] Cấu hình Hangfire với PostgreSQL storage
- [ ] Cấu hình Swagger/OpenAPI với JWT auth support
- [ ] Cấu hình Rate Limiting (ASP.NET Core built-in)

### AI Service Project Init

- [ ] Tạo FastAPI project structure theo `ARCHITECTURE.md`
- [ ] Setup Alembic cho AI DB migrations
- [ ] Cấu hình structlog
- [ ] Cấu hình internal API key authentication middleware
- [ ] Setup `pytest` với `pytest-asyncio`

---

## Phase 1 — Domain 1 & 2: Auth & User Profile

### Database

- [ ] Migration: tạo bảng `users`
- [ ] Migration: tạo bảng `refresh_tokens`
- [ ] Migration: tạo bảng `audit_logs`
- [ ] Migration: tạo bảng `data_deletion_requests`
- [ ] Seed: tạo admin user mặc định

### Backend — Auth Module

- [ ] Entity: `User`, `RefreshToken`, `AuditLog`
- [ ] Repository: `IUserRepository`, `IRefreshTokenRepository`
- [ ] Command: `RegisterCommand` + Handler + Validator
- [ ] Command: `LoginCommand` + Handler + Validator
- [ ] Command: `RefreshTokenCommand` + Handler
- [ ] Command: `LogoutCommand` + Handler
- [ ] Command: `LogoutAllDevicesCommand` + Handler
- [ ] Command: `ChangePasswordCommand` + Handler + Validator
- [ ] Command: `DeleteAccountCommand` + Handler
- [ ] Controller: `AuthController` với tất cả endpoints
- [ ] Middleware: JWT authentication cấu hình
- [ ] Service: `TokenService` (generate, validate, hash JWT/refresh token)
- [ ] Service: `AuditLogService` (ghi audit events)
- [ ] Job: `DataDeletionJob` (hard delete sau 30 ngày)

### Backend — User Profile Module

- [ ] Query: `GetUserProfileQuery` + Handler
- [ ] Command: `UpdateUserProfileCommand` + Handler + Validator
- [ ] Command: `UpdateNotificationPrefsCommand` + Handler
- [ ] Controller: `UsersController`

### Tests

- [ ] Unit: `RegisterCommandHandler` tests (email duplicate, password validation)
- [ ] Unit: `LoginCommandHandler` tests (wrong password, locked account)
- [ ] Unit: `RefreshTokenCommandHandler` tests (rotation, reuse detection)
- [ ] Integration: `AuthController` endpoints

---

## Phase 2 — Domain 3: Financial Accounts

### Database

- [ ] Migration: tạo bảng `provider_configs`
- [ ] Migration: tạo bảng `financial_accounts`
- [ ] Seed: system provider configs (MB Bank, Vietcombank, MoMo, ZaloPay, VNPay)

### Backend

- [ ] Entity: `FinancialAccount`, `ProviderConfig`
- [ ] Repository: `IFinancialAccountRepository`
- [ ] Command: `CreateFinancialAccountCommand` + Handler + Validator
- [ ] Command: `UpdateFinancialAccountCommand` + Handler
- [ ] Command: `ToggleAccountMonitoringCommand` + Handler
- [ ] Command: `DeleteFinancialAccountCommand` + Handler (soft)
- [ ] Query: `GetAccountListQuery` + Handler
- [ ] Query: `GetAccountBalanceQuery` + Handler (tính từ transactions)
- [ ] Controller: `FinancialAccountsController`

### Tests

- [ ] Unit: duplicate package_name per user validation
- [ ] Unit: không xóa account đang có transactions

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
| Phase 0 — Setup | `[ ]` | 0 / 15 |
| Phase 1 — Auth & Profile | `[ ]` | 0 / 24 |
| Phase 2 — Financial Accounts | `[ ]` | 0 / 11 |
| Phase 3 — Categories | `[ ]` | 0 / 8 |
| Phase 4 — Notifications & Transactions | `[ ]` | 0 / 28 |
| Phase 5 — Budget & Goals | `[ ]` | 0 / 22 |
| Phase 6 — Reports | `[ ]` | 0 / 12 |
| Phase 7 — Gamification | `[ ]` | 0 / 20 |
| Phase 8 — Admin | `[ ]` | 0 / 12 |
| Phase 9 — AI Service | `[ ]` | 0 / 24 |
| **Total** | | **0 / 176** |

---

*Last updated: 2026-09-09*
*Next priority: Phase 0 — Project Setup*
