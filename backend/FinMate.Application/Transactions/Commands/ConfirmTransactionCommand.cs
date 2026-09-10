namespace FinMate.Application.Transactions.Commands;

public record ConfirmTransactionCommand(Guid UserId, Guid TransactionId);
