namespace FinMate.Application.Transactions.Queries;

public record GetTransactionDetailQuery(Guid UserId, Guid TransactionId);
