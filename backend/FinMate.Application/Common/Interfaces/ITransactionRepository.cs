using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

public record TransactionListFilter(
    Guid UserId,
    Guid? FinancialAccountId,
    Guid? CategoryId,
    TransactionType? TransactionType,
    TransactionStatus? Status,
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

    /// <summary>
    /// Tổng chi tiêu (chỉ Debit, chỉ Confirmed) trong nửa mở [from, to). <paramref name="categoryId"/>
    /// null = mọi category. Dùng để backfill budget_periods.spent_cents khi user tạo budget giữa
    /// chu kỳ — không có nó, budget mới luôn hiện 0 dù user đã tiêu trong tháng.
    /// </summary>
    Task<long> SumConfirmedSpendAsync(
        Guid userId,
        Guid? categoryId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);
}
