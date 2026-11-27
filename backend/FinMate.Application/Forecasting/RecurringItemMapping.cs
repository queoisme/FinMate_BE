using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

internal static class RecurringItemMapping
{
    public static RecurringItemView ToView(this RecurringItem item) => new(
        item.Id,
        item.Direction,
        item.Name,
        item.CounterpartyKey,
        item.FinancialAccountId,
        item.AmountCents,
        item.Cadence,
        item.NextDueDate,
        RecurringSchedule.DayOf(item),
        item.TotalOccurrences is { } total ? Math.Max(0, total - item.PaidOccurrences) : null,
        item.EndDate);

    /// <summary>
    /// Ngày gốc trong tháng, suy từ ngày đến hạn — KHÔNG từ ngày thanh toán (trả sớm hai ngày không được làm lịch trôi theo).
    /// Đến hạn đúng ngày cuối tháng thì giữ "cuối tháng", để khoản ngày 31 về 28/2 rồi quay lại 31/3 thay vì kẹt ở 28.
    /// </summary>
    internal static int? PreferredDay(RecurringCadence cadence, DateOnly nextDue)
        => cadence == RecurringCadence.Weekly
            ? null
            : nextDue.Day == DateTime.DaysInMonth(nextDue.Year, nextDue.Month) ? LastDayOfMonth : nextDue.Day;

    private const int LastDayOfMonth = 31;

    public static string Text<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    /// <summary>DTO kèm tiến độ kỳ cuối tính ra từ entity (Phase 92, BR-INS-10).</summary>
    public static RecurringItemDto ToDto(this RecurringItem i, DateOnly today)
    {
        var progress = InstallmentProgress.Of(i, today);
        return new RecurringItemDto(
            i.Id,
            Text(i.Direction),
            i.Name,
            i.CounterpartyKey,
            i.CategoryId,
            i.FinancialAccountId,
            i.AmountCents,
            Text(i.Cadence),
            i.NextDueDate,
            Text(i.Status),
            Text(i.Source),
            i.Occurrences,
            i.LastSeenAt,
            i.LastSeenAmountCents,
            i.CreatedAt,
            i.TotalOccurrences,
            i.PrepaidOccurrences,
            progress.Paid,
            progress.Remaining,
            progress.RemainingAmountCents,
            i.EndDate,
            i.AnchorDay,
            progress.FinalDueDate,
            i.CompletedAt,
            i.SettledEarly);
    }
}
