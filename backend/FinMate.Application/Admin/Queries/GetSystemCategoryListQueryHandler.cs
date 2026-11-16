using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Queries;

public class GetSystemCategoryListQueryHandler : IGetSystemCategoryListQueryHandler
{
    private readonly ICategoryRepository _categoryRepository;

    public GetSystemCategoryListQueryHandler(ICategoryRepository categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    public async Task<PagedResult<AdminCategoryDto>> HandleAsync(
        GetSystemCategoryListQuery query, CancellationToken ct = default)
    {
        var categories = await _categoryRepository.GetSystemListAsync(query.IncludeInactive, ct);
        var matched = categories
            .Where(c => AdminPaging.Matches(query.Search, c.Name, c.Slug))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.Id);
        return AdminPaging.Page(matched, query.Offset, query.Limit).Map(AdminMapper.ToDto);
    }
}
