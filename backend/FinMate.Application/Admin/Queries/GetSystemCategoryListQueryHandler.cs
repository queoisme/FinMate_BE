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

    public async Task<IReadOnlyList<AdminCategoryDto>> HandleAsync(
        GetSystemCategoryListQuery query, CancellationToken ct = default)
    {
        var categories = await _categoryRepository.GetSystemListAsync(query.IncludeInactive, ct);
        return categories.Select(AdminMapper.ToDto).ToList();
    }
}
