using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions;

namespace FinMate.Application.Transactions.Queries;

public class GetTransactionDetailQueryHandler : IGetTransactionDetailQueryHandler
{
    private readonly ITransactionRepository _transactionRepository;

    public GetTransactionDetailQueryHandler(ITransactionRepository transactionRepository)
    {
        _transactionRepository = transactionRepository;
    }

    public async Task<TransactionDto> HandleAsync(GetTransactionDetailQuery query, CancellationToken ct = default)
    {
        var transaction = await _transactionRepository.GetByIdAsync(query.TransactionId, query.UserId, ct)
            ?? throw new NotFoundException("Transaction", query.TransactionId);

        // Phase 81: khoản chi đã xác nhận kèm tổng đã hoàn và phần còn hoàn được.
        long? refunded = RefundableShape(transaction)
            ? await _transactionRepository.SumConfirmedRefundsAsync(query.UserId, transaction.Id, null, ct)
            : null;
        return TransactionMapper.ToDto(transaction, refunded);
    }

    internal static bool RefundableShape(Domain.Entities.Transaction transaction)
        => transaction.TransactionType == Domain.Enums.TransactionType.Debit && transaction.Status == Domain.Enums.TransactionStatus.Confirmed;
}
