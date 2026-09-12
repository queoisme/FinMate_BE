namespace FinMate.Application.Admin.Queries;

public record GetAuditLogListQuery(
    Guid? UserId,
    string? EventType,
    DateTimeOffset? FromDate,
    DateTimeOffset? ToDate,
    string? Cursor,
    int Limit);
