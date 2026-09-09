# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Start here

Read `AGENTS.md` first — it is the authoritative agent config for this repo (mandatory reading order, hard rules, scope boundaries, escalation policy). This file only adds Claude-Code-specific notes and a quick architecture/command reference; it does not restate `AGENTS.md`.

Then read, in order: `.context/ARCHITECTURE.md`, `.context/TECH_STACK.md`, `.context/CONVENTIONS.md`, and the relevant section of `.context/TASKS.md`.

## Current state

The repo is skeleton-only (Phase 0, 0/176 tasks in `.context/TASKS.md`) — directories exist with placeholder `README.md` files but no actual source code, solution/project files, or `requirements.txt` yet. There is no build, lint, or test tooling to run until Phase 0 scaffolding tasks are done. When starting Phase 0 work, follow `TECH_STACK.md` and `ARCHITECTURE.md` exactly for project/package layout — don't improvise structure.

Out of scope entirely: `android/` (separate mobile team, not present in this checkout).

## Repo layout

- `backend/` — ASP.NET Core 9 Web API, layered: `FinMate.API` (Controllers/Middleware) → `FinMate.Application` (Commands/Queries, hand-rolled CQRS — no MediatR) → `FinMate.Domain` (Entities/Enums/ValueObjects, no dependencies) → `FinMate.Infrastructure` (EF Core, Repositories, Redis, Hangfire jobs, AI Service HTTP client).
- `ai-service/` — Python 3.11 FastAPI. Pipeline: `Classifier → Extractor → Categorizer → Duplicate Detector`, orchestrated in `app/pipeline/orchestrator.py`. Own PostgreSQL DB (`finmate_ai`), fully isolated from the backend DB — no FKs across them, only `_hash` fields.

Full directory trees and the Backend↔AI-Service API contract are in `.context/ARCHITECTURE.md` §2–3; don't re-derive them, they're already there.

## Commands (once scaffolded — not yet runnable)

**Backend** (from `backend/`):
```
dotnet build
dotnet test
dotnet test --filter FullyQualifiedName~ClassName.MethodName   # single test
dotnet ef migrations add <PascalCaseDescriptiveName> --project FinMate.Infrastructure --startup-project FinMate.API
```

**AI Service** (from `ai-service/`):
```
pytest
pytest tests/unit/test_classifier.py::test_name   # single test
alembic upgrade head
alembic revision -m "description"
black . && ruff check . && isort .
```

## Non-obvious cross-cutting rules

These are easy to violate by accident because they aren't enforced by types; see `AGENTS.md` §3 for the full list, but the ones most likely to bite mid-task:

- Money is always `BIGINT` cents (VND) — never `decimal`/`double`/`float`.
- IDs are always `Guid`/UUID — never `int`.
- `DateTimeOffset`/`TIMESTAMPTZ` everywhere — never bare `DateTime`/`TIMESTAMP`.
- AI DB never stores real `user_id` — always `SHA-256(user_id)`, and never logs `notification_body`, `amount_cents`, passwords, or tokens.
- Repository methods must filter by `userId` — never fetch a resource by ID alone (cross-user data leak risk); get `userId` from JWT claims, never from request body/path.
- Balances/summaries are pre-computed aggregates, not recomputed from all transactions on every read.
