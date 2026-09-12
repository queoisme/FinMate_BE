using FinMate.Domain.Enums;

namespace FinMate.Application.Admin.Queries;

public record GetAdminUserListQuery(
    string? Search,
    UserRole? Role,
    bool? IsLocked,
    bool IncludeDeleted,
    string? Cursor,
    int Limit);
