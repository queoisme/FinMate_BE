using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Commands;

public class DeleteTransactionCommandHandler : IDeleteTransactionCommandHandler
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IFinancialAccountRepository _financialAccountRepository;
    private readonly IBudgetPeriodService _budgetPeriodService;
    private readonly ICacheService _cache;

    public DeleteTransactionCommandHandler(
        ITransactionRepository transactionRepository,
        IFinancialAccountRepository financialAccountRepository,
        IBudgetPeriodService budgetPeriodService,
        ICacheService cache)
    {
        _transactionRepository = transactionRepository;
        _financialAccountRepository = financialAccountRepository;
        _budgetPeriodService = budgetPeriodService;
        _cache = cache;
    }

    // TODO [!] Blocked by Phase 7: revert EXP khi xóa giao dịch đã confirmed.
    public async Task HandleAsync(DeleteTransactionCommand command, CancellationToken ct = default)
    {
        var transaction = await _transactionRepository.GetByIdAsync(command.TransactionId, command.UserId, ct)
            ?? throw new NotFoundException("Transaction", command.TransactionId);

        var now = DateTimeOffset.UtcNow;
        var wasConfirmed = transaction.Status == TransactionStatus.Confirmed;

        if (wasConfirmed)
        {
            var account = await _financialAccountRepository.GetByIdAsync(transaction.FinancialAccountId, command.UserId, ct)
                ?? throw new NotFoundException("FinancialAccount", transaction.FinancialAccountId);
            account.BalanceCents -= transaction.TransactionType == TransactionType.Credit
                ? transaction.AmountCents
                : -transaction.AmountCents;
            account.UpdatedAt = now;

            await _budgetPeriodService.ApplyDeltaAsync(
                command.UserId,
                transaction.CategoryId,
                -TransactionBudgetDelta.Spend(transaction.TransactionType, transaction.AmountCents),
                transaction.TransactedAt,
                ct);
        }

        transaction.DeletedAt = now;
        transaction.UpdatedAt = now;

        // account và budget period (nếu có) đã tracked cùng DbContext — UpdateAsync flush
        // atomically, xem ghi chú trong ConfirmTransactionCommandHandler.
        await _transactionRepository.UpdateAsync(transaction, ct);

        if (wasConfirmed)
        {
            await TransactionBudgetDelta.InvalidateSummaryAsync(_cache, command.UserId, transaction.TransactedAt, ct);
        }
    }
}
