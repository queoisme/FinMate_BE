namespace FinMate.Application.Common.Models;

public record BudgetDto(
    Guid Id,
    Guid? CategoryId,
    string? CategoryName,
    string? CategorySlug,
    long LimitCents,
    string PeriodType);

public record BudgetSummaryItemDto(
    Guid BudgetId,
    Guid? CategoryId,
    string? CategoryName,
    string? CategorySlug,
    long LimitCents,
    long SpentCents,
    long RemainingCents,
    int PercentUsed,
    bool IsOverLimit);

public record BudgetSummaryDto(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    long TotalLimitCents,
    long TotalSpentCents,
    IReadOnlyList<BudgetSummaryItemDto> Items);
