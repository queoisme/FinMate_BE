namespace FinMate.Application.Transactions.Commands;

public record DeleteTransactionCommand(Guid UserId, Guid TransactionId);
