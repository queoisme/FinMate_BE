namespace FinMate.Application.Budgets.Commands;

public interface IDeleteBudgetCommandHandler
{
    Task HandleAsync(DeleteBudgetCommand command, CancellationToken ct = default);
}
