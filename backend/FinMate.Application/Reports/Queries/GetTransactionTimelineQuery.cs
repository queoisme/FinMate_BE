namespace FinMate.Application.Reports.Queries;

public record GetTransactionTimelineQuery(
    Guid UserId,
    DateTimeOffset? FromDate,
    DateTimeOffset? ToDate,
    string? Cursor,
    int Limit);
