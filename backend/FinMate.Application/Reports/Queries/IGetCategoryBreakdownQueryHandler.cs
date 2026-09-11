using FinMate.Application.Common.Models;

namespace FinMate.Application.Reports.Queries;

public interface IGetCategoryBreakdownQueryHandler
{
    Task<CategoryBreakdownDto> HandleAsync(GetCategoryBreakdownQuery query, CancellationToken ct = default);
}
