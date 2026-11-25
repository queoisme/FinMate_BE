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
    private readonly Loans.ILoanTransactionView? _loans;

    private readonly IReceiptScanRepository? _scans;

    public GetTransactionDetailQueryHandler(
        ITransactionRepository transactionRepository,
        IReceiptScanRepository? scans = null,
        Refunds.IRefundSuggestions? suggestions = null,
        Duplicates.IDuplicateDetector? duplicates = null,
        Transfers.ITransferSuggestions? transfers = null,
        Loans.ILoanTransactionView? loans = null)
    {
        _loans = loans;
        _transactionRepository = transactionRepository;
        _scans = scans;
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
        var loans = _loans is null ? null : await _loans.ViewAsync(query.UserId, [transaction], ct);
        var dto = TransactionMapper.ToDto(
            transaction,
            refunded,
            suggestions is not null && suggestions.TryGetValue(transaction.Id, out var s) ? s.Items : null,
            duplicates?.GetValueOrDefault(transaction.Id),
            transfers?.GetValueOrDefault(transaction.Id)).WithLoan(loans?.GetValueOrDefault(transaction.Id));

        if (_scans is not null && transaction.Source == Domain.Enums.TransactionSource.Receipt
            && await _scans.GetLinkedScanIdAsync(transaction.Id, query.UserId, ct) is { } scanId)
        {
            var scan = await _scans.GetByIdAsync(scanId, query.UserId, ct);
            return dto with { ReceiptScanId = scanId, AiClassification = DraftInsights.ForScan(scan) };
        }

        if (transaction.NotificationLogId is { } logId)
        {
            var result = await _transactionRepository.GetNotificationAiResultAsync(query.UserId, logId, ct);
            return dto with { AiClassification = DraftInsights.ForNotification(result) };
        }

        return dto;
    }

    internal static bool RefundableShape(Domain.Entities.Transaction transaction)
        => transaction.TransactionType == Domain.Enums.TransactionType.Debit && transaction.Status == Domain.Enums.TransactionStatus.Confirmed;
}
