using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Categories.Queries;

public class GetCategoryListQueryHandler : IGetCategoryListQueryHandler
{
    private readonly ICategoryRepository _categoryRepository;

    public GetCategoryListQueryHandler(ICategoryRepository categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    public async Task<IReadOnlyList<CategoryDto>> HandleAsync(GetCategoryListQuery query, CancellationToken ct = default)
    {
        var categories = await _categoryRepository.GetListForUserAsync(query.UserId, query.IncludeHidden, ct);
        var hidden = query.IncludeHidden ? await _categoryRepository.GetHiddenIdsAsync(query.UserId, ct) : [];

        return categories
            .OrderByDescending(c => c.IsSystem)
            .ThenBy(c => c.Name)
            .Select(c => CategoryDto.From(c).WithHidden(hidden.Contains(c.Id)))
            .ToList();
    }
}
