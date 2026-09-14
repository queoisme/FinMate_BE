using FinMate.Application.Common.Models;

namespace FinMate.Application.Transactions.Commands;

public interface ICreateTransferCommandHandler
{
    Task<TransactionCreationResult> HandleAsync(CreateTransferCommand command, CancellationToken ct = default);
}
