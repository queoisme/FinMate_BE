# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Start here

Read `AGENTS.md` first — it is the authoritative agent config for this repo (mandatory reading order, hard rules, scope boundaries, escalation policy). This file only adds Claude-Code-specific notes and a quick architecture/command reference; it does not restate `AGENTS.md`.

Then read, in order: `.context/ARCHITECTURE.md`, `.context/TECH_STACK.md`, `.context/CONVENTIONS.md`, and the relevant section of `.context/TASKS.md`.

## Current state

**Backend: every MVP phase done (309/311 tasks in `.context/TASKS.md`).** Full ASP.NET Core 9 solution with unit + integration tests (Testcontainers), 8 Hangfire recurring jobs, 11 public controllers plus 7 admin controllers under `/api/v1`. Auth, Users, FinancialAccounts, Categories, Notifications, Transactions, Budgets, SavingGoals, Reports and Gamification are all implemented and verified end-to-end via `docker compose up` + curl (see the Verify notes at the bottom of `TASKS.md`).

**Admin (Phase 8).** Six controllers under `/api/v1/admin/*`, all behind the `AdminOnly` policy inherited from `AdminControllerBase` — never repeat the attribute per controller, a missed one falls back to "any logged-in user" and still answers 200. Nothing is hard-deleted: provider configs, system categories and missions are switched off with `is_active`. Three identifiers are immutable after creation (`provider_key`, a system category's `slug`, a mission's `code`) because other systems join on them; changing one breaks the link silently rather than loudly. Every admin write lands in `audit_logs` via the `AuditEvents` constants; `/admin/audit-logs` is read-only.

**AI Service: implemented and serving (Phase 9).** `POST /api/v1/analyze`, `POST /api/v1/feedback`, `GET /api/v1/stats` and `POST /api/v1/ocr` are live; `POST /notifications/analyze` on the backend now returns a real draft transaction instead of 503. Pipeline is **rule-based extraction + ML classification**: the Extractor uses per-provider regex stored in the `provider_patterns` table (bank notifications are fixed templates — regex is both more accurate and traceable, and money must never be a model's guess), while Classifier and Categorizer load versioned scikit-learn models through `model_registry` and fall back to rules when nothing is promoted. `scripts/` holds the full lifecycle: `seed_dataset` → `train` → `evaluate` → `promote` (which refuses a model with no test-split evaluation or accuracy below 0.70) → `feedback_batch`. 12 AI DB tables via 4 alembic migrations; the 3 `ab_*` tables are deliberately deferred.

**Four input channels (Phase 10).** Bank notification (Flow 1), plus the three of Flow 2 that reach the server: typed natural language, voice, and receipt photo. Voice needs no new AI route — the client does speech-to-text and posts the text to `/transactions/parse` — but it does need `app/utils/number_words.py`, because speech-to-text returns what people say ("bốn mươi lăm ngàn"), not digits. That parser matches on **accented** text: stripping Vietnamese accents merges mười(10) with mươi(×10), từ with tư(4), and "công ty" with tỷ. Receipt photos go to `POST /api/v1/ocr` (Tesseract, `--psm 6`, in-memory only — images are never stored anywhere). `transactions.source` records which channel was used, and a client may not claim `Notification`; that value gates the AI quality metric on `/admin/ai-stats`.

**Core Flow 1 & 2 re-checked against the docx (Phase 11).** `TASKS.md` had been measured against itself, not against `FinMate_Core_User_Flows.docx`; reading Flow 1 and Flow 2 end to end exposed five gaps, now closed. The one that matters most: budget threshold alerts used to wait for the hourly `BudgetAlertJob`, so someone crossing 90% could sit 59 minutes before the Mascot reacted. Threshold logic now lives in `BudgetAlertEvaluator` and runs on the transaction itself — but `ApplyDeltaAsync` deliberately does not `SaveChangesAsync`, so it **returns** the alerts and each handler pushes them after its own save; pushing inside it would announce a transaction that may still roll back. The job stays as a safety net for the two paths that cross a threshold with no transaction at all (lowering a limit, and backfilling `spent_cents` when a budget is created). `IBudgetAlertNotifier.IsEnabledAsync` is asked **before** the `Alert*SentAt` flag is closed — marking a flag for someone who has alerts off eats that threshold permanently. The other four: `GET /transactions?status=Draft` (the "Chờ duyệt" tab), `users.monthly_income_cents` (expected income, unlike the report's actual `Credit` total which is 0 in month one), `GET /financial-accounts/providers` (without it a client had to send a `providerConfigId` it had no way to learn), and Techcombank/VPBank/ShopeePay seeded **inactive** until someone checks their `package_name` against a real notification.

**Real push via FCM (Phase 12).** `FirebaseAdmin` was approved on 2026-09-14, so `IPushNotificationService` now reaches actual handsets. Device tokens live in `device_tokens`, unique **on the token alone**: a token belongs to exactly one user, and registering it under a second account moves the row rather than adding one — leaving the old row would keep pushing the new owner's financial alerts to the previous owner's phone. `FcmPushNotificationService` is the single choke point for `NotificationPrefs.PushEnabled`, because two of the four push call sites never checked it (harmless while everything was log-only, a real leak the moment the channel went live). Only `Unregistered`/`SenderIdMismatch` prune a token — a network blip is temporary and deleting on it severs a live device for good. Nothing ever throws to the caller: every push sits behind an already-saved transaction. Never log the token (it is a capability) or the body (the bank-notification branch puts the amount in it); when a send reaches nobody, log a **warning** with the error code plus the leading exception type from FCM's message, never the rest of it. With no `FCM_CREDENTIALS_PATH`/`FCM_CREDENTIALS_JSON` the app falls back to `LoggingPushNotificationService`, and the startup log says which mode is active.

**User governance (Phase 13).** Two operational holes, not spec gaps — the docx has no admin flow at all, so nothing was there to measure against. `PATCH /admin/users/{id}/role` exists because the only way to create an admin used to be the `ADMIN_SEED_*` environment variables read at startup. It refuses to change your own role (even upward — self-promotion makes the audit trail meaningless), to demote the last **active** admin (locked admins don't count: they cannot log in), and to promote an account awaiting deletion. Demotion revokes refresh tokens because the role lives in the JWT and is not re-read per request, so an old token still says `Admin` until it expires; promotion needs no revocation since `RefreshTokenCommandHandler` reloads the user from the DB. `GET /admin/data-deletion-requests` plus `POST .../{id}/cancel` put eyes on the hard-delete queue that `DataDeletionJob` drains at 03:00. Cancelling must clear `user.DeletedAt` — the requester is soft-deleted the moment they ask, so they cannot log in to undo it themselves and this is the **only** recovery path. There is deliberately no delete-now endpoint. `AdminUserSeeder` now repairs the seed account on every boot (role, lock, soft delete — never the password): it used to only check whether the email existed, which meant the documented "fix the env and restart" escape hatch did not actually work.

**Offline sync and rate limiting (Phase 14).** The docx requires queuing notifications in Room while offline and syncing when the network returns; the Room half lives in `android/`, but the server has to survive that sync and did not. Notification dedup is now keyed on `contentHash` alone with **no time window** — the hash already encodes `ReceivedAt` rounded to the minute, and the old window measured `CreatedAt` (server insert time), so a replay five minutes later slipped through and created a duplicate draft. The `Ignored` branch deliberately keeps a short window: a notification skipped because the wallet wasn't monitored must be re-evaluated if the user adds that wallet before syncing. Manual transactions have nothing to hash — two identical coffees in the same minute are two real purchases — so `POST /transactions` and `/transactions/transfer` accept an optional client-generated `clientRequestId`, guarded by `UNIQUE (user_id, client_request_id) WHERE client_request_id IS NOT NULL`, and a replay answers **200** rather than 201. Rate limiting existed as two policies that were never attached to anything, so `UseRateLimiter()` limited nothing; the default limit is now a `GlobalLimiter` partitioned **by user id** (falling back to IP when anonymous), which is why `UseRateLimiter()` must run after `UseAuthentication()`. Attaching the default via `MapControllers().RequireRateLimiting()` silently overrode `[EnableRateLimiting]` on the login endpoints — measured at 120/min instead of 10 — which is what `GlobalLimiter` avoids.

**The 85% branch (Phase 15).** The last missing piece of Core Flow 1. Which branch fires is decided by `min(extraction, categorization)` — one tap confirms the *whole* record, so the weakest link governs; a 0.70 extraction with a 0.95 categorisation must not one-tap a possibly-wrong amount. The classifier's score is excluded: it answers "is this a transaction at all", a gate already passed before a draft exists. A null on either side means *not* confident. The threshold is 85% per the step-by-step flow — the docx contradicts itself, its edge-case table says 80% for the same behaviour — and lives in one constant. Making it work required raising the AI service's lexicon-hit confidence from 0.80 to 0.90: at 0.80 every *spend* fell to the dialog branch and only income ever got one tap, which is the opposite of the docx's own Highlands Coffee example. Pushes now carry a `data` payload (`action`, `transactionId`, `amountCents`, …) because text alone tells a client a transaction arrived but not which one to confirm; that payload is **never logged**, same rule as `notification_body`. `POST /transactions/{id}/confirm` takes an optional `categoryId` so step 5.3 is genuinely one call, and the category is applied **before** `ApplyDeltaAsync` so the budget charged is the one the user chose; a correction is fed back to the AI as `category_correction`.

**Deployment shape (Phase 16).** The image is multi-stage now — `publish -c Release` into `aspnet`, running as `$APP_UID`, 359MB instead of 2.86GB — where it used to ship the SDK and rebuild from source on every container start. `Directory.Build.props` must be copied before `restore`: no `.csproj` carries `TargetFramework`. Serilog only writes files under Development; `/app` is not writable by the non-root user and logs inside a container are unreadable anyway. Outside Development the app **refuses to start** if `JWT_SECRET`, `AI_SERVICE_API_KEY`, `ADMIN_SEED_PASSWORD` or `HANGFIRE_DASHBOARD_PASS` still contains `change-me`, because that value sits in the committed `.env.example` and an unchanged JWT secret lets anyone who reads the repo mint admin tokens. `TRUSTED_PROXIES` controls `UseForwardedHeaders`: empty means off, which is the safe default — behind a proxy without it the login rate limit partitions on the *proxy's* address, so everyone shares one bucket; with it trusting everything, a client can forge `X-Forwarded-For` to escape the limit. Note the trap: leaving `KnownNetworks`/`KnownProxies` empty means trust *nobody*, so "trust all" needs an explicit `0.0.0.0/0` and `::/0`. `/health/live` and `/health/ready` are separate because a failing readiness should drain traffic while a failing liveness should restart the container; the old `/health` answered "healthy" with Postgres down.

**Remaining:** nothing blocked; Core Flow 1 and Flow 2 are now complete server-side. Two tasks are deliberately skipped (OTP registration, the three `ab_*` tables). **Core Flow 3 and Core Flow 4 have never been checked against the docx** — Phase 11 only read Flow 1 and Flow 2 end to end, so the task count proves nothing about those two.

Out of scope entirely: `android/` (separate mobile team, not present in this checkout).

## Repo layout

- `backend/` — ASP.NET Core 9 Web API, layered: `FinMate.API` (Controllers/Middleware) → `FinMate.Application` (Commands/Queries, hand-rolled CQRS — no MediatR) → `FinMate.Domain` (Entities/Enums/ValueObjects, no dependencies) → `FinMate.Infrastructure` (EF Core, Repositories, Redis, Hangfire jobs, AI Service HTTP client).
- `ai-service/` — Python 3.11 FastAPI. Pipeline: `Classifier → Extractor → Categorizer → Duplicate Detector`, orchestrated in `app/pipeline/orchestrator.py`. Own PostgreSQL DB (`finmate_ai`), fully isolated from the backend DB — no FKs across them, only `_hash` fields.

Full directory trees and the Backend↔AI-Service API contract are in `.context/ARCHITECTURE.md` §2–3; don't re-derive them, they're already there.

## Commands

**Backend** (from `backend/`):
```
dotnet build
dotnet test
dotnet test --filter FullyQualifiedName~ClassName.MethodName   # single test
dotnet ef migrations add <PascalCaseDescriptiveName> --project FinMate.Infrastructure --startup-project FinMate.API
```

**AI Service** (from `ai-service/`):
```
pytest                                            # integration tests need Postgres, else skipped
pytest tests/unit/test_classifier.py::test_name   # single test
alembic upgrade head
alembic revision -m "description"
black . && ruff check . && isort .

python scripts/train.py --stage classifier        # → candidate
python scripts/evaluate.py --stage classifier --version 1.0.0
python scripts/promote.py --stage classifier --version 1.0.0   # → active, hot-reloaded in 60s
```

Two requirements files: `requirements.txt` is what the serving image installs;
`requirements-training.txt` adds torch/transformers/underthesea for offline training only.
OCR needs the `tesseract-ocr` and `tesseract-ocr-vie` OS packages — the Dockerfile installs
them, so OCR only works inside the container, and tests fake the engine.

## Non-obvious cross-cutting rules

These are easy to violate by accident because they aren't enforced by types; see `AGENTS.md` §3 for the full list, but the ones most likely to bite mid-task:

- Money is always `BIGINT` cents (VND) — never `decimal`/`double`/`float`.
- IDs are always `Guid`/UUID — never `int`.
- `DateTimeOffset`/`TIMESTAMPTZ` everywhere — never bare `DateTime`/`TIMESTAMP`.
- AI DB never stores real `user_id` — always `SHA-256(user_id)`, and never logs `notification_body`, `amount_cents`, passwords, or tokens.
- Repository methods must filter by `userId` — never fetch a resource by ID alone (cross-user data leak risk); get `userId` from JWT claims, never from request body/path.
- Balances/summaries are pre-computed aggregates, not recomputed from all transactions on every read.
