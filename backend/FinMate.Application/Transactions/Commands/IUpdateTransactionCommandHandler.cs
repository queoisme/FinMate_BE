using FinMate.Application.Common.Models;

namespace FinMate.Application.Transactions.Commands;

public interface IUpdateTransactionCommandHandler
{
    Task<TransactionDto> HandleAsync(UpdateTransactionCommand command, CancellationToken ct = default);
}
