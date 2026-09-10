using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;

namespace FinMate.Application.Transactions;

internal static class TransactionMapper
{
    public static TransactionDto ToDto(Transaction transaction) => new(
        transaction.Id,
        transaction.FinancialAccountId,
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
        transaction.CreatedAt);
}
