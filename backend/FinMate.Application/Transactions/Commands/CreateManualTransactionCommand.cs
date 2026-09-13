using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Commands;

/// <param name="Source">
/// Kênh người dùng dùng để nhập: <c>Manual</c> (form/NLP), <c>Voice</c>, hay <c>Receipt</c>.
/// <c>Notification</c> bị TỪ CHỐI ở validator — xem lý do ở đó.
/// </param>
public record CreateManualTransactionCommand(
    Guid UserId,
    Guid FinancialAccountId,
    Guid? CategoryId,
    long AmountCents,
    TransactionType TransactionType,
    DateTimeOffset TransactedAt,
    string? MerchantName,
    string? Description,
    TransactionSource Source = TransactionSource.Manual);
