using FinMate.Application.Common.Models;

namespace FinMate.Application.Transactions.Queries;

public interface IGetTransactionListQueryHandler
{
    Task<TransactionListDto> HandleAsync(GetTransactionListQuery query, CancellationToken ct = default);
}
