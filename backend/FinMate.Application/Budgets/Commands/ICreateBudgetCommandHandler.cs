using FinMate.Application.Common.Models;

namespace FinMate.Application.Budgets.Commands;

public interface ICreateBudgetCommandHandler
{
    Task<BudgetDto> HandleAsync(CreateBudgetCommand command, CancellationToken ct = default);
}
