namespace FinMate.Application.Admin.Queries;

/// <param name="From">null = 30 ngày gần nhất.</param>
public record GetAiStatsQuery(DateTimeOffset? From, DateTimeOffset? To);
