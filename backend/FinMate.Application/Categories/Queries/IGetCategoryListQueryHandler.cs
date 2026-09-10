using FinMate.Application.Common.Models;

namespace FinMate.Application.Categories.Queries;

public interface IGetCategoryListQueryHandler
{
    Task<IReadOnlyList<CategoryDto>> HandleAsync(GetCategoryListQuery query, CancellationToken ct = default);
}
