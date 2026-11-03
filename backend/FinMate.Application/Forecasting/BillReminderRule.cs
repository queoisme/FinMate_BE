using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

public enum BillReminderKind
{
    /// <summary>Sắp đến hạn, chưa thấy thanh toán.</summary>
    DueSoon,

    /// <summary>Quá hạn lâu (hết ân hạn) mà chưa thấy thanh toán: tạm ngừng theo dõi và hỏi user còn dùng không.</summary>
    LongOverdue,
}

public sealed record BillReminder(RecurringItemView Item, DateOnly DueDate, BillReminderKind Kind, int DaysOverdue);

/// <summary>Trang 7 của sơ đồ ("Theo dõi và nhắc hạn hóa đơn định kỳ"), chạy mỗi ngày trên các khoản chi đang theo dõi.</summary>
public static class BillReminderRule
{
    public static IReadOnlyList<BillReminder> Evaluate(
        IReadOnlyList<RecurringItemView> activeItems,
        IReadOnlyList<ForecastTransaction> recent,
        DateOnly today,
        int daysBefore,
        ForecastRules rules)
    {
        var reminders = new List<BillReminder>();
        foreach (var item in activeItems.Where(i => i.Direction == RecurringDirection.Outflow))
        {
            var due = item.NextDueDate;
            if (RecurringMatcher.FindPayment(item, due, recent, rules) is not null)
            {
                continue;
            }

            if (due >= today && due <= today.AddDays(daysBefore))
            {
                reminders.Add(new BillReminder(item, due, BillReminderKind.DueSoon, 0));
            }
            else if (due < today.AddDays(-rules.OverdueGraceDays))
            {
                reminders.Add(new BillReminder(item, due, BillReminderKind.LongOverdue, today.DayNumber - due.DayNumber));
            }
        }

        return reminders;
    }
}
