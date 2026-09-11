using FinMate.Application.Common.Models;

namespace FinMate.Application.Reports.Queries;

public interface IGetMonthlySummaryQueryHandler
{
    Task<MonthlySummaryDto> HandleAsync(GetMonthlySummaryQuery query, CancellationToken ct = default);
}
