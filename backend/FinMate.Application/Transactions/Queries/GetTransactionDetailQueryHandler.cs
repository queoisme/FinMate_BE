using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions;

namespace FinMate.Application.Transactions.Queries;

public class GetTransactionDetailQueryHandler : IGetTransactionDetailQueryHandler
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly Refunds.IRefundSuggestions? _suggestions;

    public GetTransactionDetailQueryHandler(ITransactionRepository transactionRepository, Refunds.IRefundSuggestions? suggestions = null)
    {
        _transactionRepository = transactionRepository;
        _suggestions = suggestions;
    }

    public async Task<TransactionDto> HandleAsync(GetTransactionDetailQuery query, CancellationToken ct = default)
    {
        var transaction = await _transactionRepository.GetByIdAsync(query.TransactionId, query.UserId, ct)
            ?? throw new NotFoundException("Transaction", query.TransactionId);

        // Phase 81: khoản chi đã xác nhận kèm tổng đã hoàn và phần còn hoàn được.
        long? refunded = RefundableShape(transaction)
            ? await _transactionRepository.SumConfirmedRefundsAsync(query.UserId, transaction.Id, null, ct)
            : null;
        var suggestions = _suggestions is null ? null : await _suggestions.ForDraftsAsync(query.UserId, [transaction], ct);
        return TransactionMapper.ToDto(
            transaction, refunded, suggestions is not null && suggestions.TryGetValue(transaction.Id, out var s) ? s.Items : null);
    }

    internal static bool RefundableShape(Domain.Entities.Transaction transaction)
        => transaction.TransactionType == Domain.Enums.TransactionType.Debit && transaction.Status == Domain.Enums.TransactionStatus.Confirmed;
}
