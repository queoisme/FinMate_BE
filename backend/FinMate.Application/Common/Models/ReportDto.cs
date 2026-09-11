namespace FinMate.Application.Common.Models;

public record MonthlySummaryDto(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    long TotalSpentCents,
    long TotalIncomeCents,
    long NetCents,
    int TransactionCount,
    long PrevMonthSpentCents,
    double? ChangePercent);

public record CategoryBreakdownItemDto(
    Guid? CategoryId,
    string CategoryName,
    string? CategorySlug,
    long SpentCents,
    int Percent);

public record CategoryBreakdownDto(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    long TotalSpentCents,
    IReadOnlyList<CategoryBreakdownItemDto> Items);

public record TimelineDayDto(
    DateOnly Date,
    long TotalSpentCents,
    long TotalIncomeCents,
    IReadOnlyList<TransactionDto> Transactions);

public record TimelineDto(IReadOnlyList<TimelineDayDto> Days, string? NextCursor);

/// <summary>
/// <paramref name="Confidence"/> low/medium/high theo số ngày có dữ liệu — để client không
/// trình bày một con số dựng từ vài ngày như thể nó chắc chắn.
/// </summary>
public record SpendingForecastDto(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    long SpentSoFarCents,
    long ProjectedSpendCents,
    int DaysElapsed,
    int DaysInMonth,
    int BasedOnDays,
    string Confidence);

public record SpendingInsightDto(
    Guid Id,
    string InsightType,
    string Title,
    string Body,
    Guid? CategoryId,
    string? CategoryName,
    long? AmountCents,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    bool IsRead,
    DateTimeOffset CreatedAt);
