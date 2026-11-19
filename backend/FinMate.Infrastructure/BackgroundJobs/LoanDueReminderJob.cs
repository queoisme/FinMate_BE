using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// Nhắc khoản vay/cho vay sắp đến hạn (trong 3 ngày) hoặc đã quá hạn mà chưa tất toán. Nhắc đúng một lần cho mỗi hạn cuối (<c>due_notified_at</c>;
/// đổi hạn thì được nhắc lại). Cờ được đánh dấu cả khi người dùng tắt push, vì đó là cờ chống nhắc lặp. Thông báo không chứa số tiền.
/// </summary>
public class LoanDueReminderJob(ILoanRepository loans, IUserRepository users, IPushNotificationService push)
{
    public const int DaysAhead = 3;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var now = DateTimeOffset.UtcNow;
        foreach (var loan in await loans.GetDueUnnotifiedAsync(today.AddDays(DaysAhead), ct))
        {
            var user = await users.GetByIdAsync(loan.UserId, ct);
            if (user is not null && user.NotificationPrefs.PushEnabled)
            {
                var overdue = loan.DueDate < today;
                var borrow = loan.Direction == LoanDirection.Borrow;
                var title = overdue ? "Khoản vay đã quá hạn" : "Khoản vay sắp đến hạn";
                var body = borrow
                    ? $"Khoản \"{loan.Title}\" {(overdue ? "đã quá hạn trả" : "sắp đến hạn trả")}."
                    : $"Khoản cho vay \"{loan.Title}\" {(overdue ? "đã quá hạn thu hồi" : "sắp đến hạn thu hồi")}.";
                await push.NotifyAsync(loan.UserId, title, body, ct: ct);
            }

            loan.DueNotifiedAt = now;
            await loans.UpdateAsync(loan, ct);
        }
    }
}
