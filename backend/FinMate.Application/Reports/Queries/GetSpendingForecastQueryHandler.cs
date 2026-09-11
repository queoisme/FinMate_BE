using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Reports.Queries;

public class GetSpendingForecastQueryHandler : IGetSpendingForecastQueryHandler
{
    private readonly ISpendingForecaster _forecaster;

    public GetSpendingForecastQueryHandler(ISpendingForecaster forecaster)
    {
        _forecaster = forecaster;
    }

    public Task<SpendingForecastDto> HandleAsync(GetSpendingForecastQuery query, CancellationToken ct = default)
        => _forecaster.ForecastCurrentMonthAsync(query.UserId, ct);
}
