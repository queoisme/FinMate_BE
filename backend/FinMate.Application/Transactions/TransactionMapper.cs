using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;

namespace FinMate.Application.Transactions;

internal static class TransactionMapper
{
    /// <param name="refundedCents">Tổng hoàn đã xác nhận nếu đây là khoản chi đã xác nhận (caller đọc); null thì hai trường hoàn để trống.</param>
    /// <param name="duplicates">Phase 82: giao dịch được trỏ tới và gợi ý trùng (caller đọc theo lô); null thì hai trường đó để trống.</param>
    public static TransactionDto ToDto(
        Transaction transaction,
        long? refundedCents = null,
        IReadOnlyList<RefundSuggestionDto>? suggestions = null,
        DuplicateView? duplicates = null,
        TransferSuggestionDto? transfer = null) => new(
        transaction.Id,
        transaction.FinancialAccountId,
        transaction.CounterAccountId,
        transaction.CategoryId,
        transaction.Category?.Name,
        transaction.AmountCents,
        transaction.TransactionType.ToString(),
        transaction.Source.ToString(),
        transaction.Status.ToString(),
        transaction.MerchantName,
        transaction.Description,
        transaction.TransactedAt,
        transaction.BalanceAfterCents,
        transaction.CreatedAt,
        transaction.GroupId,
        transaction.Relation?.ToString(),
        transaction.RelatedTransactionId,
        transaction.IsReversed,
        refundedCents,
        refundedCents is { } refunded ? Refunds.RefundPolicy.RemainingRefundable(transaction, refunded) : null,
        suggestions,
        transaction.DuplicateStatus?.ToString(),
        duplicates?.Of,
        duplicates?.Suggested,
        transaction.MergedSources,
        transfer);
}
