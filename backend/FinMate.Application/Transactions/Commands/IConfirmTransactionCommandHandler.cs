using FinMate.Application.Common.Models;

namespace FinMate.Application.Transactions.Commands;

public interface IConfirmTransactionCommandHandler
{
    Task<TransactionDto> HandleAsync(ConfirmTransactionCommand command, CancellationToken ct = default);
}
