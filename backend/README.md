# FinMate Backend — ASP.NET Core 9

Layered architecture (API → Application → Domain → Infrastructure).
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

Chưa scaffold code — xem `../.context/TASKS.md` Phase 0 để bắt đầu (tạo `FinMate.sln`, cấu hình Npgsql, Serilog, Hangfire, Swagger, Rate Limiting).
