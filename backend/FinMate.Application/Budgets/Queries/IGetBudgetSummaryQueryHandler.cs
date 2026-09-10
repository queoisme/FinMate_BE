using FinMate.Application.Common.Models;

namespace FinMate.Application.Budgets.Queries;

public interface IGetBudgetSummaryQueryHandler
{
    Task<BudgetSummaryDto> HandleAsync(GetBudgetSummaryQuery query, CancellationToken ct = default);
}
