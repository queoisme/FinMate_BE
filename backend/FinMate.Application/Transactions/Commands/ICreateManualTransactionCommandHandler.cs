using FinMate.Application.Common.Models;

namespace FinMate.Application.Transactions.Commands;

public interface ICreateManualTransactionCommandHandler
{
    Task<TransactionCreationResult> HandleAsync(CreateManualTransactionCommand command, CancellationToken ct = default);
}
