namespace FinMate.Application.Transactions.Commands;

public interface IDeleteTransactionCommandHandler
{
    Task HandleAsync(DeleteTransactionCommand command, CancellationToken ct = default);
}
