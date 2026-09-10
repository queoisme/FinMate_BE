using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Queries;

public record GetTransactionListQuery(
    Guid UserId,
    Guid? FinancialAccountId,
    Guid? CategoryId,
    TransactionType? TransactionType,
    DateTimeOffset? FromDate,
    DateTimeOffset? ToDate,
    string? Cursor,
    int Limit);
