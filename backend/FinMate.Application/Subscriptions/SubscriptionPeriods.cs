using FinMate.Domain.Enums;

namespace FinMate.Application.Subscriptions;

public static class SubscriptionPeriods
{
    public static DateTimeOffset EndOf(DateTimeOffset start, SubscriptionCycle cycle)
        => cycle == SubscriptionCycle.Monthly ? start.AddMonths(1) : start.AddYears(1);

    /// <summary>Kỳ tiếp theo bắt đầu ngay khi kỳ cuối hết, nên gia hạn và đổi gói không làm khách mất ngày nào đã trả.</summary>
    public static DateTimeOffset NextStart(DateTimeOffset now, DateTimeOffset? lastEnd)
        => lastEnd is { } end && end > now ? end : now;

    /// <summary>Đơn càng cũ càng ít có khả năng còn tiền về, nên hỏi SePay thưa dần.</summary>
    public static TimeSpan CheckIntervalFor(TimeSpan orderAge)
    {
        if (orderAge < TimeSpan.FromHours(1))
        {
            return TimeSpan.FromMinutes(10);
        }

        return orderAge < TimeSpan.FromHours(24) ? TimeSpan.FromHours(1) : TimeSpan.FromHours(6);
    }

    /// <summary>Thời lượng các kỳ sau được dời lên khi một kỳ bị hủy: phần chưa dùng của kỳ đó.</summary>
    public static TimeSpan UnusedOf(DateTimeOffset start, DateTimeOffset end, DateTimeOffset now)
    {
        var from = start > now ? start : now;
        return end > from ? end - from : TimeSpan.Zero;
    }
}
