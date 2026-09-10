using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

public record TransactionListFilter(
    Guid UserId,
    Guid? FinancialAccountId,
    Guid? CategoryId,
    TransactionType? TransactionType,
    DateTimeOffset? FromDate,
    DateTimeOffset? ToDate,
    string? Cursor,
    int Limit);

public record TransactionListResult(IReadOnlyList<Transaction> Items, string? NextCursor);

public interface ITransactionRepository
{
    Task<Transaction?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<TransactionListResult> GetListAsync(TransactionListFilter filter, CancellationToken ct = default);
    Task AddAsync(Transaction transaction, CancellationToken ct = default);
    Task UpdateAsync(Transaction transaction, CancellationToken ct = default);
    Task<bool> HasAnyForAccountAsync(Guid financialAccountId, CancellationToken ct = default);
    Task<bool> HasAnyForCategoryAsync(Guid categoryId, CancellationToken ct = default);
}
