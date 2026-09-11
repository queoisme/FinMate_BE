using FinMate.Application.Common.Models;

namespace FinMate.Application.Reports.Queries;

public interface IGetSpendingInsightsQueryHandler
{
    Task<IReadOnlyList<SpendingInsightDto>> HandleAsync(GetSpendingInsightsQuery query, CancellationToken ct = default);
}
