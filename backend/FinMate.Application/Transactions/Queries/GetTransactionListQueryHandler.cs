using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions;

namespace FinMate.Application.Transactions.Queries;

public class GetTransactionListQueryHandler : IGetTransactionListQueryHandler
{
    private const int MaxLimit = 100;
    private const int DefaultLimit = 20;

    private readonly ITransactionRepository _transactionRepository;
    private readonly Refunds.IRefundSuggestions? _suggestions;

    public GetTransactionListQueryHandler(ITransactionRepository transactionRepository, Refunds.IRefundSuggestions? suggestions = null)
    {
        _transactionRepository = transactionRepository;
        _suggestions = suggestions;
    }

    public async Task<TransactionListDto> HandleAsync(GetTransactionListQuery query, CancellationToken ct = default)
    {
        var limit = query.Limit <= 0 ? DefaultLimit : Math.Min(query.Limit, MaxLimit);

        var filter = new TransactionListFilter(
            query.UserId,
            query.FinancialAccountId,
            query.CategoryId,
            query.TransactionType,
            query.Status,
            query.FromDate,
            query.ToDate,
            query.Cursor,
            limit,
            Relation: query.Relation);

        var result = await _transactionRepository.GetListAsync(filter, ct);

        // Phase 81: tổng đã hoàn của các khoản chi đã xác nhận (một truy vấn cho cả trang) và gợi ý khoản chi gốc cho nháp có hint.
        var refundable = result.Items.Where(GetTransactionDetailQueryHandler.RefundableShape).Select(t => t.Id).ToList();
        var refunded = await _transactionRepository.SumConfirmedRefundsAsync(query.UserId, refundable, ct);
        var suggestions = _suggestions is null
            ? new Dictionary<Guid, (Domain.Enums.TransactionRelation, IReadOnlyList<RefundSuggestionDto>)>()
            : await _suggestions.ForDraftsAsync(query.UserId, result.Items, ct);

        return new TransactionListDto(
            result.Items.Select(t => TransactionMapper.ToDto(
                t,
                GetTransactionDetailQueryHandler.RefundableShape(t) ? refunded.GetValueOrDefault(t.Id) : null,
                suggestions.TryGetValue(t.Id, out var s) ? s.Items : null)).ToList(),
            result.NextCursor);
    }
}
