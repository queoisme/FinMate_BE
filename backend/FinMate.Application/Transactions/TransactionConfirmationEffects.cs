using FinMate.Application.Budgets;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Application.Notifications;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions;

public class TransactionConfirmationEffects : ITransactionConfirmationEffects
{
    private readonly IBudgetPeriodService _budgetPeriodService;
    private readonly IBudgetAlertNotifier _budgetAlertNotifier;
    private readonly ICacheService _cache;
    private readonly IUserNotificationService? _userNotifications;

    public TransactionConfirmationEffects(
        IBudgetPeriodService budgetPeriodService,
        IBudgetAlertNotifier budgetAlertNotifier,
        ICacheService cache,
        IUserNotificationService? userNotifications = null)
    {
        _budgetPeriodService = budgetPeriodService;
        _budgetAlertNotifier = budgetAlertNotifier;
        _cache = cache;
        _userNotifications = userNotifications;
    }

    public async Task<IReadOnlyList<BudgetAlert>> ApplyAsync(
        Transaction transaction, FinancialAccount account, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        account.BalanceCents += transaction.TransactionType == TransactionType.Credit
            ? transaction.AmountCents
            : -transaction.AmountCents;
        account.UpdatedAt = now;

        transaction.Status = TransactionStatus.Confirmed;
        transaction.UpdatedAt = now;

        return await _budgetPeriodService.ApplyDeltaAsync(
            transaction.UserId,
            transaction.CategoryId,
            TransactionBudgetDelta.Spend(transaction.TransactionType, transaction.AmountCents, transaction.Category),
            transaction.TransactedAt,
            ct);
    }

    public async Task NotifyAsync(
        Transaction transaction, IReadOnlyList<BudgetAlert> budgetAlerts, CancellationToken ct = default)
    {
        await TransactionBudgetDelta.InvalidateSummaryAsync(_cache, transaction.UserId, transaction.TransactedAt, ct);
        await _budgetAlertNotifier.SendAsync(budgetAlerts, ct);
        await NotifyBalanceChangeAsync(transaction, ct);
    }

    /// <summary>Ghi vào hộp thư trong app (tab "Biến động") rằng giao dịch này vừa đổi số dư.</summary>
    private Task NotifyBalanceChangeAsync(Transaction transaction, CancellationToken ct)
    {
        if (_userNotifications is null)
        {
            return Task.CompletedTask;
        }

        var verb = transaction.TransactionType == TransactionType.Credit ? "Nhận" : "Chi";
        return _userNotifications.CreateAsync(
            transaction.UserId,
            UserNotificationType.BalanceChange,
            "Giao dịch mới",
            $"{verb} {ForecastText.Money(transaction.AmountCents)}"
            + (transaction.MerchantName is { Length: > 0 } merchant ? $" tại {merchant}" : string.Empty),
            "Transaction",
            transaction.Id,
            ct);
    }
}
