using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Commands;

public class ConfirmTransactionCommandHandler : IConfirmTransactionCommandHandler
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IFinancialAccountRepository _financialAccountRepository;
    private readonly IBudgetPeriodService _budgetPeriodService;
    private readonly ICacheService _cache;

    public ConfirmTransactionCommandHandler(
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

    public async Task<TransactionDto> HandleAsync(ConfirmTransactionCommand command, CancellationToken ct = default)
    {
        var transaction = await _transactionRepository.GetByIdAsync(command.TransactionId, command.UserId, ct)
            ?? throw new NotFoundException("Transaction", command.TransactionId);

        if (transaction.Status != TransactionStatus.Draft)
        {
            throw new BusinessRuleException(
                TransactionErrorCodes.NotDraft,
                "Giao dịch đã được xác nhận hoặc không còn ở trạng thái nháp.");
        }

        var account = await _financialAccountRepository.GetByIdAsync(transaction.FinancialAccountId, command.UserId, ct)
            ?? throw new NotFoundException("FinancialAccount", transaction.FinancialAccountId);

        var now = DateTimeOffset.UtcNow;
        account.BalanceCents += transaction.TransactionType == TransactionType.Credit
            ? transaction.AmountCents
            : -transaction.AmountCents;
        account.UpdatedAt = now;

        transaction.Status = TransactionStatus.Confirmed;
        transaction.UpdatedAt = now;

        await _budgetPeriodService.ApplyDeltaAsync(
            command.UserId,
            transaction.CategoryId,
            TransactionBudgetDelta.Spend(transaction.TransactionType, transaction.AmountCents),
            transaction.TransactedAt,
            ct);

        // account và budget period đều đã được EF Core track (cùng DbContext scoped với
        // ITransactionRepository) — UpdateAsync bên dưới gọi SaveChangesAsync 1 lần duy nhất,
        // flush cả ba trong cùng 1 DB transaction ngầm định của EF Core. Đây là cách đạt
        // atomicity mà không cần thêm abstraction Unit-of-Work mới.
        //
        // TODO [!] Blocked by Phase 7: cộng EXP, check streak, trigger mission condition —
        // chưa có Gamification module.
        await _transactionRepository.UpdateAsync(transaction, ct);

        await TransactionBudgetDelta.InvalidateSummaryAsync(_cache, command.UserId, transaction.TransactedAt, ct);

        return TransactionMapper.ToDto(transaction);
    }
}
