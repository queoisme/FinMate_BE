using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Commands;

public record UpdateTransactionCommand(
    Guid UserId,
    Guid TransactionId,
    Guid FinancialAccountId,
    Guid? CategoryId,
    long AmountCents,
    TransactionType TransactionType,
    DateTimeOffset TransactedAt,
    string? MerchantName,
    string? Description);
