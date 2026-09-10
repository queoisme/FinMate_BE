namespace FinMate.Application.Budgets.Commands;

public record DeleteBudgetCommand(Guid UserId, Guid BudgetId);
