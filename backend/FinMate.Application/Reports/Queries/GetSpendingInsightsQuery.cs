namespace FinMate.Application.Reports.Queries;

public record GetSpendingInsightsQuery(Guid UserId, bool UnreadOnly, int Limit);
