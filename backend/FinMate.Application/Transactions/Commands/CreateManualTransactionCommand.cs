using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Commands;

public record CreateManualTransactionCommand(
    Guid UserId,
    Guid FinancialAccountId,
    Guid? CategoryId,
    long AmountCents,
    TransactionType TransactionType,
    DateTimeOffset TransactedAt,
    string? MerchantName,
    string? Description);
