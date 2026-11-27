using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <summary>
/// Tiến độ của khoản có kỳ cuối (Phase 92, BR-INS-10). Mọi số đều tính ra từ entity, không lưu.
/// </summary>
/// <param name="Remaining">Số kỳ còn lại; null với khoản không có kỳ cuối.</param>
/// <param name="RemainingAmountCents">Ước tính = còn lại × số tiền hiện tại (không tính lãi, phạt, đổi giá).</param>
/// <param name="FinalDueDate">Ngày đến hạn của kỳ cuối cùng còn phải trả; null khi không có kỳ cuối hoặc không còn kỳ nào.</param>
public sealed record InstallmentProgress(int Paid, int? Remaining, long? RemainingAmountCents, DateOnly? FinalDueDate)
{
    // Trần vòng lặp: end_date tối đa 50 năm, khoản tuần ~2.600 kỳ.
    private const int MaxScheduleSteps = 3_000;

    public static InstallmentProgress Of(RecurringItem item, DateOnly today)
    {
        var view = item.ToView();
        if (item.TotalOccurrences is null && item.EndDate is null)
        {
            return new InstallmentProgress(item.PaidOccurrences, null, null, null);
        }

        var dates = RecurringSchedule.DueDates(view).Take(MaxScheduleSteps).ToList();
        var remaining = dates.Count;
        DateOnly? final = dates.Count == 0 ? null : dates[^1];

        // S1: kỳ chi trả góp quá hạn được chiếu vào ngày mai, nên kỳ cuối không bao giờ trước ngày mai.
        if (final is { } last && view.CountsOccurrences && item.Direction == RecurringDirection.Outflow && last < today.AddDays(1))
        {
            final = today.AddDays(1);
        }

        return new InstallmentProgress(item.PaidOccurrences, remaining, remaining * item.AmountCents, final);
    }
}

/// <summary>
/// Hoàn thành / mở lại khoản có kỳ cuối (Phase 92, BR-INS-06). Mọi đường làm đổi số kỳ đã trả, kỳ cuối hay ngày đến hạn gọi hàm này trước khi
/// lưu, trong cùng lượt lưu.
/// </summary>
public static class RecurringCompletion
{
    /// <returns>true nếu trạng thái đổi.</returns>
    public static bool Apply(RecurringItem item, DateOnly today, DateTimeOffset now)
    {
        var shouldComplete = ShouldComplete(item, today);
        if (item.Status == RecurringStatus.Completed)
        {
            if (shouldComplete || (item.SettledEarly && HasEnd(item)))
            {
                return false;
            }

            item.Status = RecurringStatus.Active;
            item.CompletedAt = null;
            item.SettledEarly = false;
            item.UpdatedAt = now;
            return true;
        }

        if (!shouldComplete || item.Status is not (RecurringStatus.Active or RecurringStatus.Paused))
        {
            return false;
        }

        item.Status = RecurringStatus.Completed;
        item.CompletedAt = ToMicroseconds(now);
        item.UpdatedAt = now;
        return true;
    }

    /// <summary>Postgres giữ tới micro giây: cắt trước khi lưu để lần đọc lại (và lần gọi lại idempotent) trả đúng cùng giá trị.</summary>
    public static DateTimeOffset ToMicroseconds(DateTimeOffset value) => value.AddTicks(-(value.Ticks % 10));

    /// <summary>Có tổng số kỳ: đã trả ≥ tổng. Có ngày kết thúc: kỳ kế tiếp đã qua ngày đó (mọi kỳ đã trả) hoặc hôm nay đã qua ngày đó (D9).</summary>
    public static bool ShouldComplete(RecurringItem item, DateOnly today)
        => item.TotalOccurrences is { } total
            ? item.PaidOccurrences >= total
            : item.EndDate is { } end && (item.NextDueDate > end || today > end);

    public static bool HasEnd(RecurringItem item) => item.TotalOccurrences is not null || item.EndDate is not null;
}
