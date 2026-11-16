using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions;

namespace FinMate.Application.Transactions.Queries;

public class GetTransactionDetailQueryHandler : IGetTransactionDetailQueryHandler
{
    private readonly ITransactionRepository _transactionRepository;

    private readonly IReceiptScanRepository? _scans;

    public GetTransactionDetailQueryHandler(ITransactionRepository transactionRepository, IReceiptScanRepository? scans = null)
    {
        _transactionRepository = transactionRepository;
        _scans = scans;
    }

    public async Task<TransactionDto> HandleAsync(GetTransactionDetailQuery query, CancellationToken ct = default)
    {
        var transaction = await _transactionRepository.GetByIdAsync(query.TransactionId, query.UserId, ct)
            ?? throw new NotFoundException("Transaction", query.TransactionId);

        var dto = TransactionMapper.ToDto(transaction);
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
}
