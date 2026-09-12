# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Start here

Read `AGENTS.md` first — it is the authoritative agent config for this repo (mandatory reading order, hard rules, scope boundaries, escalation policy). This file only adds Claude-Code-specific notes and a quick architecture/command reference; it does not restate `AGENTS.md`.

Then read, in order: `.context/ARCHITECTURE.md`, `.context/TECH_STACK.md`, `.context/CONVENTIONS.md`, and the relevant section of `.context/TASKS.md`.

## Current state

**Backend: Phase 0–9 done (253/261 tasks in `.context/TASKS.md`).** Full ASP.NET Core 9 solution with unit + integration tests (Testcontainers), 8 Hangfire recurring jobs, 10 public controllers plus 6 admin controllers under `/api/v1`. Auth, Users, FinancialAccounts, Categories, Notifications, Transactions, Budgets, SavingGoals, Reports and Gamification are all implemented and verified end-to-end via `docker compose up` + curl (see the Verify notes at the bottom of `TASKS.md`).

**Admin (Phase 8).** Six controllers under `/api/v1/admin/*`, all behind the `AdminOnly` policy inherited from `AdminControllerBase` — never repeat the attribute per controller, a missed one falls back to "any logged-in user" and still answers 200. Nothing is hard-deleted: provider configs, system categories and missions are switched off with `is_active`. Three identifiers are immutable after creation (`provider_key`, a system category's `slug`, a mission's `code`) because other systems join on them; changing one breaks the link silently rather than loudly. Every admin write lands in `audit_logs` via the `AuditEvents` constants; `/admin/audit-logs` is read-only.

**AI Service: implemented and serving (Phase 9).** `POST /api/v1/analyze`, `POST /api/v1/feedback` and `GET /api/v1/stats` are live; `POST /notifications/analyze` on the backend now returns a real draft transaction instead of 503. Pipeline is **rule-based extraction + ML classification**: the Extractor uses per-provider regex stored in the `provider_patterns` table (bank notifications are fixed templates — regex is both more accurate and traceable, and money must never be a model's guess), while Classifier and Categorizer load versioned scikit-learn models through `model_registry` and fall back to rules when nothing is promoted. `scripts/` holds the full lifecycle: `seed_dataset` → `train` → `evaluate` → `promote` (which refuses a model with no test-split evaluation or accuracy below 0.70) → `feedback_batch`. 12 AI DB tables via 4 alembic migrations; the 3 `ab_*` tables are deliberately deferred.

**Remaining:** Phase 10 decision #4 — Voice + Receipt OCR (5 tasks; OCR needs user approval for a new route in the Backend↔AI-Service contract, per `AGENTS.md` §5), plus one `[!]` task blocked on an unapproved FCM dependency (`IPushNotificationService` is still `LoggingPushNotificationService`, log-only).

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

## Non-obvious cross-cutting rules

These are easy to violate by accident because they aren't enforced by types; see `AGENTS.md` §3 for the full list, but the ones most likely to bite mid-task:

- Money is always `BIGINT` cents (VND) — never `decimal`/`double`/`float`.
- IDs are always `Guid`/UUID — never `int`.
- `DateTimeOffset`/`TIMESTAMPTZ` everywhere — never bare `DateTime`/`TIMESTAMP`.
- AI DB never stores real `user_id` — always `SHA-256(user_id)`, and never logs `notification_body`, `amount_cents`, passwords, or tokens.
- Repository methods must filter by `userId` — never fetch a resource by ID alone (cross-user data leak risk); get `userId` from JWT claims, never from request body/path.
- Balances/summaries are pre-computed aggregates, not recomputed from all transactions on every read.
