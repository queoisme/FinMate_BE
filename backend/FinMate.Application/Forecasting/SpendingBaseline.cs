using FinMate.Application.Reports;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

public enum ForecastConfidence
{
    Low,
    Normal,
}

/// <summary>
/// Mức chi tiêu LINH HOẠT mỗi ngày (không gồm khoản định kỳ), theo thứ trong tuần: <see cref="Expected"/> là trung bình (Phase 97; trước đó là trung vị),
/// <see cref="Low"/>/<see cref="High"/> là đường dự kiến ∓ độ rộng P25/P75 cũ — tạo khoảng dao động cho đường số dư. Chỉ số mảng là <see cref="DayOfWeek"/>.
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
/// (khoản đó đã được engine đặt riêng vào đúng ngày đến hạn — tính thêm ở đây là trừ hai lần; Phase 97: kể cả lần trả của một kỳ đã đổi giá, xem
/// <see cref="RecurringPaymentFilter"/>). Thiếu dữ liệu thì theo đúng sơ đồ:
/// ngân sách đã thiết lập → trung bình dữ liệu đang có → 0, và mang cờ tin cậy thấp.
/// </summary>
public static class SpendingBaselineCalculator
{
    private const int DaysPerWeek = 7;

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
        var recurring = RecurringPaymentFilter.For(history, activeItems, rules);
        var flexibleByDay = history
            .Where(t => t.Type == TransactionType.Debit && !t.ExcludedFromForecast && !recurring.IsPayment(t))
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

    /// <summary>Phase 97: trung bình theo thứ, dải giữ độ rộng cũ quanh nó (xem <see cref="DailyLevels"/>).</summary>
    private static SpendingBaseline FromHistory(IReadOnlyList<(DateOnly Date, double Amount)> series, int activeDays)
    {
        var levels = DailyLevels.From(series);
        var low = levels.Expected.Zip(levels.Below, (e, b) => e - b).ToArray();
        var high = levels.Expected.Zip(levels.Above, (e, a) => e + a).ToArray();
        return new SpendingBaseline(levels.Expected, low, high, ForecastConfidence.Normal, activeDays, SpendingBaseline.BasisHistory);
    }

    private static SpendingBaseline Flat(long perDay, int activeDays, string basis)
    {
        var values = Enumerable.Repeat(perDay, DaysPerWeek).ToArray();
        return new SpendingBaseline(values, values.ToArray(), values.ToArray(), ForecastConfidence.Low, activeDays, basis);
    }

    /// <summary>Phân vị theo nội suy tuyến tính giữa hai điểm kề nhau.</summary>
    internal static double Quantile(IReadOnlyList<double> values, double q) => Statistics.Quantile(values, q);

}
