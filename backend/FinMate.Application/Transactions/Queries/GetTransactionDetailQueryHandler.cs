using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions;

namespace FinMate.Application.Transactions.Queries;

public class GetTransactionDetailQueryHandler : IGetTransactionDetailQueryHandler
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly Refunds.IRefundSuggestions? _suggestions;
    private readonly Duplicates.IDuplicateDetector? _duplicates;
    private readonly Transfers.ITransferSuggestions? _transfers;

    public GetTransactionDetailQueryHandler(
        ITransactionRepository transactionRepository,
        Refunds.IRefundSuggestions? suggestions = null,
        Duplicates.IDuplicateDetector? duplicates = null,
        Transfers.ITransferSuggestions? transfers = null)
    {
        _transactionRepository = transactionRepository;
        _suggestions = suggestions;
        _duplicates = duplicates;
        _transfers = transfers;
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
        var duplicates = _duplicates is null ? null : await _duplicates.ViewAsync(query.UserId, [transaction], ct);
        var transfers = _transfers is null ? null : await _transfers.ForAsync(query.UserId, [transaction], ct);
        return TransactionMapper.ToDto(
            transaction,
            refunded,
            suggestions is not null && suggestions.TryGetValue(transaction.Id, out var s) ? s.Items : null,
            duplicates?.GetValueOrDefault(transaction.Id),
            transfers?.GetValueOrDefault(transaction.Id));
    }

    internal static bool RefundableShape(Domain.Entities.Transaction transaction)
        => transaction.TransactionType == Domain.Enums.TransactionType.Debit && transaction.Status == Domain.Enums.TransactionStatus.Confirmed;
}
