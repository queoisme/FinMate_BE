using FinMate.Application.Common.Models;

namespace FinMate.Application.Budgets.Commands;

public interface IUpdateBudgetLimitCommandHandler
{
    Task<BudgetDto> HandleAsync(UpdateBudgetLimitCommand command, CancellationToken ct = default);
}
