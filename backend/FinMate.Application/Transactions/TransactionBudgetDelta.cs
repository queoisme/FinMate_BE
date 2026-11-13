using FinMate.Application.Budgets;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
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
    /// cả hai đều rơi vào nhánh 0 ở đây, đây là chốt chặn duy nhất cho toàn bộ handler.
    /// Debit thuộc danh mục "Cho vay / Mượn" (<see cref="Category.LoanSlug"/>) cũng là 0: tiền cho mượn sẽ về lại nên không ăn vào hạn mức nào.
    /// Hoàn tiền (Phase 81) là Credit mang <see cref="TransactionRelation.Refund"/>: trả về số ÂM, tức hạn mức được trả lại ở kỳ của ngày hoàn
    /// (BR-RF-04). Đảo giao dịch không có tác động riêng; khoản gốc đã đảo (<paramref name="isReversed"/>) thành 0 — cặp đảo bị loại khỏi
    /// mọi tổng hợp, và việc hoàn tác khoản gốc diễn ra ở kỳ của chính nó (D1).
    /// Trả về số dương cho chi tiêu — caller đảo dấu khi cần revert.
    /// </summary>
    internal static long Spend(
        TransactionType type,
        long amountCents,
        Category? category,
        TransactionRelation? relation = null,
        bool isReversed = false)
    {
        if (category?.Slug == Category.LoanSlug)
        {
            return 0;  // cho vay/mượn không phải chi tiêu, và trả lại tiền mượn cũng không phải hoàn
        }

        return type switch
        {
            TransactionType.Debit when !isReversed => amountCents,
            TransactionType.Credit when relation == TransactionRelation.Refund => -amountCents,
            _ => 0,
        };
    }

    internal static long Spend(Transaction transaction, Category? category)
        => Spend(transaction.TransactionType, transaction.AmountCents, category, transaction.Relation, transaction.IsReversed);

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
