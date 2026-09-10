using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public interface ITransactionRepository
{
    Task<Transaction?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task AddAsync(Transaction transaction, CancellationToken ct = default);
    Task UpdateAsync(Transaction transaction, CancellationToken ct = default);
    Task<bool> HasAnyForAccountAsync(Guid financialAccountId, CancellationToken ct = default);
    Task<bool> HasAnyForCategoryAsync(Guid categoryId, CancellationToken ct = default);
}
