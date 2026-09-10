using FinMate.Application.Common.Models;

namespace FinMate.Application.Transactions.Commands;

public interface ICreateManualTransactionCommandHandler
{
    Task<TransactionDto> HandleAsync(CreateManualTransactionCommand command, CancellationToken ct = default);
}
