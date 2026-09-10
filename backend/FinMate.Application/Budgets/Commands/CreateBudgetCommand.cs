namespace FinMate.Application.Budgets.Commands;

/// <summary><paramref name="CategoryId"/> null = budget tổng cho toàn bộ chi tiêu.</summary>
public record CreateBudgetCommand(Guid UserId, Guid? CategoryId, long LimitCents);
