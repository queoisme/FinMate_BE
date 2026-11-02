using FinMate.Application.Reports;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

public enum ForecastConfidence
{
    Low,
    Normal,
}

/// <summary>
/// Mức chi tiêu LINH HOẠT mỗi ngày (không gồm khoản định kỳ), theo thứ trong tuần: <see cref="Expected"/> là trung vị, <see cref="Low"/>/<see cref="High"/>
/// là phân vị 25/75 — tạo khoảng dao động cho đường số dư. Chỉ số mảng là <see cref="DayOfWeek"/>.
/// </summary>
public sealed record SpendingBaseline(
    long[] Expected,
    long[] Low,
    long[] High,
    ForecastConfidence Confidence,
    int DaysWithData,
    string Basis)
{
    public const string BasisHistory = "history";
    public const string BasisBudget = "budget";
    public const string BasisAverage = "average";
    public const string BasisNone = "none";

    public long ExpectedOn(DateOnly date) => Expected[(int)date.DayOfWeek];
    public long LowOn(DateOnly date) => Low[(int)date.DayOfWeek];
    public long HighOn(DateOnly date) => High[(int)date.DayOfWeek];
}

/// <summary>
/// Học thói quen chi tiêu từ lịch sử. Loại khỏi dữ liệu: giao dịch user đánh dấu loại khỏi dự báo, và giao dịch khớp khoản định kỳ đang theo dõi
/// (khoản đó đã được engine đặt riêng vào đúng ngày đến hạn — tính thêm ở đây là trừ hai lần). Thiếu dữ liệu thì theo đúng sơ đồ:
/// ngân sách đã thiết lập → trung bình dữ liệu đang có → 0, và mang cờ tin cậy thấp.
/// </summary>
public static class SpendingBaselineCalculator
{
    private const int DaysPerWeek = 7;
    private const double LowerQuantile = 0.25;
    private const double UpperQuantile = 0.75;

    /// <summary>Một thứ trong tuần cần ít nhất chừng này mẫu mới dùng số riêng của nó; ít hơn thì dùng số chung của mọi ngày.</summary>
    private const int MinSamplesPerWeekday = 3;

    /// <param name="history">Giao dịch trong cửa sổ lịch sử, KHÔNG gồm hôm nay.</param>
    /// <param name="activeItems">Khoản định kỳ đang theo dõi (để loại giao dịch khớp chúng).</param>
    /// <param name="monthlyBudgetCents">Tổng hạn mức ngân sách tháng của user; 0 nếu chưa đặt.</param>
    public static SpendingBaseline Compute(
        IReadOnlyList<ForecastTransaction> history,
        IReadOnlyList<RecurringItemView> activeItems,
        long monthlyBudgetCents,
        DateOnly today,
        ForecastRules rules)
    {
        var activeDays = history.Select(t => t.Date).Distinct().Count();
        var flexibleByDay = history
            .Where(t => t.Type == TransactionType.Debit && !t.ExcludedFromForecast && !RecurringMatcher.MatchesAny(t, activeItems, rules))
            .GroupBy(t => t.Date)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.AmountCents));

        if (activeDays >= rules.MinHistoryDays)
        {
            var firstDay = history.Min(t => t.Date);
            return FromHistory(DailySeries(flexibleByDay, firstDay, today.AddDays(-1)), activeDays);
        }

        if (monthlyBudgetCents > 0)
        {
            var perDay = monthlyBudgetCents / DateTime.DaysInMonth(today.Year, today.Month);
            return Flat(perDay, activeDays, SpendingBaseline.BasisBudget);
        }

        if (history.Count > 0)
        {
            var firstDay = history.Min(t => t.Date);
            var span = Math.Max(1, today.DayNumber - firstDay.DayNumber);
            return Flat(flexibleByDay.Values.Sum() / span, activeDays, SpendingBaseline.BasisAverage);
        }

        return Flat(0, 0, SpendingBaseline.BasisNone);
    }

    /// <summary>Mỗi ngày từ <paramref name="from"/> tới <paramref name="to"/>, ngày không chi là 0 (bỏ chúng đi sẽ thổi phồng mức chi).</summary>
    private static List<(DateOnly Date, double Amount)> DailySeries(IReadOnlyDictionary<DateOnly, long> byDay, DateOnly from, DateOnly to)
    {
        var series = new List<(DateOnly, double)>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            series.Add((d, byDay.GetValueOrDefault(d)));
        }

        return series;
    }

    private static SpendingBaseline FromHistory(IReadOnlyList<(DateOnly Date, double Amount)> series, int activeDays)
    {
        var all = series.Select(s => s.Amount).ToArray();
        var expected = new long[DaysPerWeek];
        var low = new long[DaysPerWeek];
        var high = new long[DaysPerWeek];
        for (var dow = 0; dow < DaysPerWeek; dow++)
        {
            var samples = series.Where(s => (int)s.Date.DayOfWeek == dow).Select(s => s.Amount).ToArray();
            var values = Statistics.TrimUpperOutliers(samples.Length >= MinSamplesPerWeekday ? samples : all);
            expected[dow] = Round(Statistics.Median(values));
            low[dow] = Round(Quantile(values, LowerQuantile));
            high[dow] = Math.Max(expected[dow], Round(Quantile(values, UpperQuantile)));
            low[dow] = Math.Min(low[dow], expected[dow]);
        }

        return new SpendingBaseline(expected, low, high, ForecastConfidence.Normal, activeDays, SpendingBaseline.BasisHistory);
    }

    private static SpendingBaseline Flat(long perDay, int activeDays, string basis)
    {
        var values = Enumerable.Repeat(perDay, DaysPerWeek).ToArray();
        return new SpendingBaseline(values, values.ToArray(), values.ToArray(), ForecastConfidence.Low, activeDays, basis);
    }

    /// <summary>Phân vị theo nội suy tuyến tính giữa hai điểm kề nhau.</summary>
    internal static double Quantile(IReadOnlyList<double> values, double q) => Statistics.Quantile(values, q);

    private static long Round(double value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);
}
