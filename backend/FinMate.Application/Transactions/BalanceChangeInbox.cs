using FinMate.Application.Forecasting;
using FinMate.Application.Notifications;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions;

/// <summary>
/// Mục "Giao dịch mới" trong hộp thư (tab Biến động) cho một giao dịch vừa đổi số dư. Gọi SAU khi giao dịch đã lưu:
/// ghi trước là báo một giao dịch có thể còn rollback. null (test cũ): không ghi gì.
/// </summary>
public static class BalanceChangeInbox
{
    public static Task WriteAsync(IUserNotificationService? inbox, Transaction transaction, CancellationToken ct = default)
    {
        if (inbox is null)
        {
            return Task.CompletedTask;
        }

        var verb = transaction.TransactionType == TransactionType.Credit ? "Nhận" : "Chi";
        return inbox.CreateAsync(
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
