using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Queries;

public interface IGetSystemCategoryListQueryHandler
{
    Task<IReadOnlyList<AdminCategoryDto>> HandleAsync(
        GetSystemCategoryListQuery query, CancellationToken ct = default);
}
