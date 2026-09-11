using FinMate.Application.Budgets;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions;

/// <summary>
/// Quy đổi giao dịch sang delta chi tiêu của budget, dùng chung cho cả 4 handler
/// (confirm / create manual / update / delete) để chiều +/- không bị lệch giữa các nơi.
/// </summary>
internal static class TransactionBudgetDelta
{
    /// <summary>
    /// Chỉ Debit tiêu hạn mức. Credit là tiền vào nên không ăn vào budget chi tiêu; Transfer là
    /// dịch chuyển giữa 2 ví của chính user nên cũng không (ARCHITECTURE.md §0 quyết định #1) —
    /// cả hai đều rơi vào nhánh 0 ở đây, đây là chốt chặn duy nhất cho toàn bộ 4 handler.
    /// Trả về số dương — caller đảo dấu khi cần revert.
    /// </summary>
    internal static long Spend(TransactionType type, long amountCents)
        => type == TransactionType.Debit ? amountCents : 0;

    internal static Task InvalidateSummaryAsync(
        ICacheService cache,
        Guid userId,
        DateTimeOffset transactedAt,
        CancellationToken ct)
    {
        var (year, month) = VietnamTime.YearMonthOf(transactedAt);
        return cache.RemoveAsync(CacheKeys.BudgetSummary(userId, year, month), ct);
    }
}
