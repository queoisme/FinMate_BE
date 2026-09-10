using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Commands;

public class DeleteTransactionCommandHandler : IDeleteTransactionCommandHandler
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IFinancialAccountRepository _financialAccountRepository;

    public DeleteTransactionCommandHandler(
        ITransactionRepository transactionRepository,
        IFinancialAccountRepository financialAccountRepository)
    {
        _transactionRepository = transactionRepository;
        _financialAccountRepository = financialAccountRepository;
    }

    // TODO [!] Blocked by Phase 5/7: revert budget_periods, revert EXP khi xóa giao dịch đã confirmed.
    public async Task HandleAsync(DeleteTransactionCommand command, CancellationToken ct = default)
    {
        var transaction = await _transactionRepository.GetByIdAsync(command.TransactionId, command.UserId, ct)
            ?? throw new NotFoundException("Transaction", command.TransactionId);

        var now = DateTimeOffset.UtcNow;

        if (transaction.Status == TransactionStatus.Confirmed)
        {
            var account = await _financialAccountRepository.GetByIdAsync(transaction.FinancialAccountId, command.UserId, ct)
                ?? throw new NotFoundException("FinancialAccount", transaction.FinancialAccountId);
            account.BalanceCents -= transaction.TransactionType == TransactionType.Credit
                ? transaction.AmountCents
                : -transaction.AmountCents;
            account.UpdatedAt = now;
        }

        transaction.DeletedAt = now;
        transaction.UpdatedAt = now;

        // account (nếu có) đã tracked cùng DbContext — UpdateAsync flush atomically,
        // xem ghi chú trong ConfirmTransactionCommandHandler.
        await _transactionRepository.UpdateAsync(transaction, ct);
    }
}
