using FinMate.Application.Groups;
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
    private readonly IForecastAlertTrigger? _forecastAlerts;

    private readonly IGroupLedger? _groupLedger;

    public DeleteTransactionCommandHandler(
        ITransactionRepository transactionRepository,
        IFinancialAccountRepository financialAccountRepository,
        IBudgetPeriodService budgetPeriodService,
        ICacheService cache,
        IForecastAlertTrigger? forecastAlerts = null,
        IGroupLedger? groupLedger = null)
    {
        _groupLedger = groupLedger;
        _transactionRepository = transactionRepository;
        _financialAccountRepository = financialAccountRepository;
        _budgetPeriodService = budgetPeriodService;
        _cache = cache;
        _forecastAlerts = forecastAlerts;
    }

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

            // Transfer đã cộng tiền vào ví đích lúc tạo — không trả lại thì tiền tự sinh ra.
            if (transaction.CounterAccountId is not null)
            {
                var counter = await _financialAccountRepository.GetByIdAsync(transaction.CounterAccountId.Value, command.UserId, ct)
                    ?? throw new NotFoundException("FinancialAccount", transaction.CounterAccountId.Value);
                counter.BalanceCents -= transaction.AmountCents;
                counter.UpdatedAt = now;
            }

            // Xoá chỉ sinh delta ÂM nên không bao giờ chạm ngưỡng cảnh báo — bỏ qua giá trị
            // trả về là có chủ ý, không phải quên.
            await _budgetPeriodService.ApplyDeltaAsync(
                command.UserId,
                transaction.CategoryId,
                -TransactionBudgetDelta.Spend(transaction.TransactionType, transaction.AmountCents, transaction.Category),
                transaction.TransactedAt,
                ct);
        }

        // Hoàn phần của nhóm, kể cả khi chủ giao dịch đã rời nhóm: số của nhóm phải luôn khớp với các khoản còn lại.
        if (_groupLedger is not null)
        {
            await _groupLedger.ApplyAsync(transaction, transaction.Category, -1, ct);
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
        await NotifyForecastAsync(command.UserId, ct);
    }

    /// <summary>Kiểm tra số dư thấp / vượt ngân sách sớm ngoài request (Flow 6). Gọi SAU khi đã lưu; trigger tự nuốt lỗi.</summary>
    private Task NotifyForecastAsync(Guid userId, CancellationToken ct)
        => _forecastAlerts is null ? Task.CompletedTask : _forecastAlerts.UserChangedAsync(userId, ct);
}
