namespace FinMate.Application.Budgets.Commands;

public record UpdateBudgetLimitCommand(Guid UserId, Guid BudgetId, long LimitCents);
