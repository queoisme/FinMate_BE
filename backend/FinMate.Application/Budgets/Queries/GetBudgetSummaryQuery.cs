namespace FinMate.Application.Budgets.Queries;

/// <summary>Year/Month null = chu kỳ tháng hiện tại.</summary>
public record GetBudgetSummaryQuery(Guid UserId, int? Year, int? Month);
