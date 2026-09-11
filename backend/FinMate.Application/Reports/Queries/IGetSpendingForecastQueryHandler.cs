using FinMate.Application.Common.Models;

namespace FinMate.Application.Reports.Queries;

public interface IGetSpendingForecastQueryHandler
{
    Task<SpendingForecastDto> HandleAsync(GetSpendingForecastQuery query, CancellationToken ct = default);
}
