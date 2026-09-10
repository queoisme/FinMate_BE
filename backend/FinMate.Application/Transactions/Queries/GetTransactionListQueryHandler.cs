using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions;

namespace FinMate.Application.Transactions.Queries;

public class GetTransactionListQueryHandler : IGetTransactionListQueryHandler
{
    private const int MaxLimit = 100;
    private const int DefaultLimit = 20;

    private readonly ITransactionRepository _transactionRepository;

    public GetTransactionListQueryHandler(ITransactionRepository transactionRepository)
    {
        _transactionRepository = transactionRepository;
    }

    public async Task<TransactionListDto> HandleAsync(GetTransactionListQuery query, CancellationToken ct = default)
    {
        var limit = query.Limit <= 0 ? DefaultLimit : Math.Min(query.Limit, MaxLimit);

        var filter = new TransactionListFilter(
            query.UserId,
            query.FinancialAccountId,
            query.CategoryId,
            query.TransactionType,
            query.FromDate,
            query.ToDate,
            query.Cursor,
            limit);

        var result = await _transactionRepository.GetListAsync(filter, ct);

        return new TransactionListDto(result.Items.Select(TransactionMapper.ToDto).ToList(), result.NextCursor);
    }
}
