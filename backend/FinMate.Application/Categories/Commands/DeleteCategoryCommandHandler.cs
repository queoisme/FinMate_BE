using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Categories.Commands;

public class DeleteCategoryCommandHandler : IDeleteCategoryCommandHandler
{
    private readonly ICategoryRepository _categoryRepository;

    public DeleteCategoryCommandHandler(ICategoryRepository categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    // NOTE: guard "không xóa category đang có transaction" được thêm trong commit dựng
    // Transaction module (cùng lượt Phase 3+4) — xem CATEGORY_HAS_TRANSACTIONS.
    public async Task HandleAsync(DeleteCategoryCommand command, CancellationToken ct = default)
    {
        var category = await _categoryRepository.GetOwnedByUserAsync(command.CategoryId, command.UserId, ct)
            ?? throw new NotFoundException("Category", command.CategoryId);

        var now = DateTimeOffset.UtcNow;
        category.DeletedAt = now;
        category.UpdatedAt = now;

        await _categoryRepository.UpdateAsync(category, ct);
    }
}
