using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <param name="ConservativeMonthlyCents">Mức thận trọng: phân vị 25 của thu nhập theo tháng (BR-07.2).</param>
/// <param name="ReceivedThisMonthCents">Đã nhận trong tháng này (không khớp khoản thu định kỳ nào).</param>
public sealed record IrregularIncomeDto(long ConservativeMonthlyCents, long AverageMonthlyCents, int MonthsUsed, long ReceivedThisMonthCents);

public sealed record LateIncomeDto(Guid RecurringItemId, string Name, long AmountCents, DateOnly ExpectedDate, int DaysLate);

public sealed record IncomeForecastDto(
    bool IsRegular,
    IReadOnlyList<UpcomingItemDto> Upcoming,
    IReadOnlyList<LateIncomeDto> Late,
    IrregularIncomeDto? Irregular);

/// <summary>Trang 5 của sơ đồ ("Dự báo lịch thu nhập"). Thu theo mùa (BR-07.3) chưa làm: cần hơn một năm dữ liệu.</summary>
public static class IncomeEstimator
{
    private const int MinMonthsForEstimate = 2;
    private const double ConservativeQuantile = 0.25;

    /// <summary>Thu nhập đều: các lần nhận của khoản thu định kỳ trong <c>[today, to]</c>, lần nào đã thấy tiền về thì bỏ.</summary>
    public static IReadOnlyList<UpcomingItemDto> Upcoming(
        IReadOnlyList<RecurringItemView> activeItems, IReadOnlyList<ForecastTransaction> recent, DateOnly today, DateOnly to, ForecastRules rules)
        => CashflowEngine.RecurringEvents(activeItems.Where(i => i.Direction == RecurringDirection.Inflow).ToList(), recent, today, to, rules)
            .OrderBy(e => e.Date)
            .Select(e => new UpcomingItemDto(e.Date, e.Label, e.AmountCents, e.RecurringItemId))
            .ToList();

    /// <summary>Khoản thu định kỳ đã quá ngày dự kiến ít nhất <paramref name="lateDays"/> ngày mà chưa thấy tiền về (BR-07.4).</summary>
    public static IReadOnlyList<LateIncomeDto> Late(
        IReadOnlyList<RecurringItemView> activeItems, IReadOnlyList<ForecastTransaction> recent, DateOnly today, int lateDays, ForecastRules rules)
        => activeItems
            .Where(i => i.Direction == RecurringDirection.Inflow
                        && i.NextDueDate <= today.AddDays(-lateDays)
                        && RecurringMatcher.FindPayment(i, i.NextDueDate, recent, rules) is null)
            .Select(i => new LateIncomeDto(i.Id, i.Name, i.AmountCents, i.NextDueDate, today.DayNumber - i.NextDueDate.DayNumber))
            .ToList();

    /// <summary>
    /// Thu nhập không đều: tổng thu mỗi tháng TRỌN VẸN gần nhất (không tính tháng này), chỉ từ khoản thu không khớp khoản định kỳ nào.
    /// Tháng không có thu nhập vẫn tính là 0 — bỏ đi sẽ thổi phồng mức thận trọng. Dưới hai tháng dữ liệu thì không ước tính.
    /// </summary>
    public static IrregularIncomeDto? Irregular(
        IReadOnlyList<ForecastTransaction> history, IReadOnlyList<RecurringItemView> activeItems, DateOnly today, int months, ForecastRules rules)
    {
        var credits = history
            .Where(t => t.Type == TransactionType.Credit && !t.ExcludedFromForecast && !RecurringMatcher.MatchesAny(t, activeItems, rules))
            .ToList();
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var receivedThisMonth = credits.Where(t => t.Date >= thisMonth).Sum(t => t.AmountCents);
        if (history.Count == 0)
        {
            return null;
        }

        var firstData = history.Min(t => t.Date);
        var firstMonth = new DateOnly(firstData.Year, firstData.Month, 1);
        var monthStarts = Enumerable.Range(1, months)
            .Select(i => thisMonth.AddMonths(-i))
            .Where(m => m > firstMonth || (m == firstMonth && firstData.Day == 1))
            .ToList();
        if (monthStarts.Count < MinMonthsForEstimate)
        {
            return null;
        }

        var totals = monthStarts
            .Select(m => (double)credits.Where(t => t.Date >= m && t.Date < m.AddMonths(1)).Sum(t => t.AmountCents))
            .ToArray();

        return new IrregularIncomeDto(
            (long)Math.Round(SpendingBaselineCalculator.Quantile(totals, ConservativeQuantile)),
            (long)Math.Round(totals.Average()),
            totals.Length,
            receivedThisMonth);
    }
}
