using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <summary>Lịch của một khoản định kỳ: lần đến hạn kế tiếp, các lần trong một khoảng ngày, cửa sổ để nhận ra lần đã trả.</summary>
public static class RecurringSchedule
{
    private const int DaysPerWeek = 7;

    // Trả sớm tối đa bao nhiêu ngày trước hạn vẫn tính cho lần này: khoảng nửa chu kỳ, để không nhận nhầm lần trước/lần sau.
    private const int WeeklyEarlyDays = 3;
    private const int MonthlyEarlyDays = 14;
    private const int YearlyEarlyDays = 60;

    /// <summary>
    /// Lần đến hạn sau <paramref name="due"/>. Với chu kỳ tháng/năm, <paramref name="preferredDay"/> giữ ngày gốc để khoản đến hạn ngày 31
    /// về 28/2 rồi quay lại 31/3, không trôi dần thành ngày 28 mãi.
    /// </summary>
    public static DateOnly Next(DateOnly due, RecurringCadence cadence, int? preferredDay = null) => cadence switch
    {
        RecurringCadence.Weekly => due.AddDays(DaysPerWeek),
        RecurringCadence.Monthly => OnDay(due.AddMonths(1), preferredDay ?? due.Day),
        RecurringCadence.Yearly => OnDay(due.AddYears(1), preferredDay ?? due.Day),
        _ => throw new ArgumentOutOfRangeException(nameof(cadence), cadence, null),
    };

    /// <summary>Mọi lần đến hạn nằm trong <c>[from, to]</c>, tính từ <see cref="RecurringItemView.NextDueDate"/>.</summary>
    public static IEnumerable<DateOnly> Occurrences(RecurringItemView item, DateOnly from, DateOnly to)
    {
        var due = item.NextDueDate;
        while (due < from)
        {
            due = Next(due, item.Cadence, item.PreferredDay);
        }

        while (due <= to)
        {
            yield return due;
            due = Next(due, item.Cadence, item.PreferredDay);
        }
    }

    /// <summary>
    /// Khoảng ngày mà một giao dịch khớp được coi là đã trả cho lần đến hạn <paramref name="due"/>: từ nửa chu kỳ trước hạn (trả sớm)
    /// tới hết ân hạn sau hạn (trả muộn).
    /// </summary>
    public static (DateOnly From, DateOnly To) PaymentWindow(DateOnly due, RecurringCadence cadence, int graceDays)
    {
        var early = cadence switch
        {
            RecurringCadence.Weekly => WeeklyEarlyDays,
            RecurringCadence.Monthly => MonthlyEarlyDays,
            RecurringCadence.Yearly => YearlyEarlyDays,
            _ => throw new ArgumentOutOfRangeException(nameof(cadence), cadence, null),
        };
        return (due.AddDays(-early), due.AddDays(graceDays));
    }

    private static DateOnly OnDay(DateOnly monthAnchor, int day)
        => new(monthAnchor.Year, monthAnchor.Month, Math.Min(day, DateTime.DaysInMonth(monthAnchor.Year, monthAnchor.Month)));
}
