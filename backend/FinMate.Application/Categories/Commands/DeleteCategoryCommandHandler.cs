using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Categories.Commands;

public class DeleteCategoryCommandHandler : IDeleteCategoryCommandHandler
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ITransactionRepository _transactionRepository;

    public DeleteCategoryCommandHandler(ICategoryRepository categoryRepository, ITransactionRepository transactionRepository)
    {
        _categoryRepository = categoryRepository;
        _transactionRepository = transactionRepository;
    }

    public async Task HandleAsync(DeleteCategoryCommand command, CancellationToken ct = default)
    {
        var category = await _categoryRepository.GetOwnedByUserAsync(command.CategoryId, command.UserId, ct)
            ?? throw new NotFoundException("Category", command.CategoryId);

        if (await _transactionRepository.HasAnyForCategoryAsync(category.Id, ct))
        {
            throw new ConflictException(
                CategoryErrorCodes.HasTransactions,
                "Không thể xóa danh mục đang có giao dịch.");
        }

        var now = DateTimeOffset.UtcNow;
        category.DeletedAt = now;
        category.UpdatedAt = now;

        await _categoryRepository.UpdateAsync(category, ct);
    }
}
