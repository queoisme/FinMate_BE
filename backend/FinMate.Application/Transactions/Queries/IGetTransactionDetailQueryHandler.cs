using FinMate.Application.Common.Models;

namespace FinMate.Application.Transactions.Queries;

public interface IGetTransactionDetailQueryHandler
{
    Task<TransactionDto> HandleAsync(GetTransactionDetailQuery query, CancellationToken ct = default);
}
