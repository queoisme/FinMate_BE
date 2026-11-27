using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;

namespace FinMate.Backtest.Diagnostics;

/// <summary>
/// Phase 97, Task 1 (D5): ba phương án ước lượng mức theo ngày, áp cho cả chi linh hoạt (Debit) và thu linh hoạt (Credit), trên cùng chuỗi ngày mà
/// <see cref="SpendingBaselineCalculator"/> dùng (90 ngày trước mốc, không khớp khoản định kỳ, không bị loại khỏi dự báo, ngày không có = 0).
/// Chỉ đo trong harness; production chưa đổi.
/// </summary>
public static class Estimators
{
    public const string F1 = "F1 trung bình theo thứ";
    public const string F2 = "F2 trung bình theo thứ, winsorize P95";
    public const string F3 = "F3 trung bình chung";

    public static readonly IReadOnlyList<string> All = [F1, F2, F3];

    private const int DaysPerWeek = 7;
    private const int MinSamplesPerWeekday = 3;
    private const double WinsorQuantile = 0.95;

    /// <summary>Mức của từng thứ trong tuần theo mỗi phương án; null khi chưa đủ <c>minHistoryDays</c> ngày có giao dịch (nhánh thiếu lịch sử).</summary>
    public static IReadOnlyDictionary<string, long[]>? Levels(
        IReadOnlyList<ForecastTransaction> history, IReadOnlyList<RecurringItemView> items, DateOnly origin, ForecastRules rules, TransactionType type)
    {
        if (history.Count == 0 || history.Select(t => t.Date).Distinct().Count() < rules.MinHistoryDays)
        {
            return null;
        }

        var byDay = history
            .Where(t => t.Type == type && !t.ExcludedFromForecast && !RecurringMatcher.MatchesAny(t, items, rules))
            .GroupBy(t => t.Date)
            .ToDictionary(g => g.Key, g => (double)g.Sum(t => t.AmountCents));
        var series = new List<(DateOnly Date, double Amount)>();
        for (var d = history.Min(t => t.Date); d < origin; d = d.AddDays(1))
        {
            series.Add((d, byDay.GetValueOrDefault(d)));
        }

        var cap = Application.Reports.Statistics.Quantile(series.Select(s => s.Amount).ToList(), WinsorQuantile);
        var winsorized = series.Select(s => (s.Date, Amount: Math.Min(s.Amount, cap))).ToList();
        var pooled = Round(series.Average(s => s.Amount));
        return new Dictionary<string, long[]>
        {
            [F1] = ByWeekday(series),
            [F2] = ByWeekday(winsorized),
            [F3] = Enumerable.Repeat(pooled, DaysPerWeek).ToArray(),
        };
    }

    private static long[] ByWeekday(IReadOnlyList<(DateOnly Date, double Amount)> series)
    {
        var all = series.Select(s => s.Amount).ToArray();
        var levels = new long[DaysPerWeek];
        for (var dow = 0; dow < DaysPerWeek; dow++)
        {
            var samples = series.Where(s => (int)s.Date.DayOfWeek == dow).Select(s => s.Amount).ToArray();
            levels[dow] = Round((samples.Length >= MinSamplesPerWeekday ? samples : all).Average());
        }

        return levels;
    }

    private static long Round(double v) => (long)Math.Round(v, MidpointRounding.AwayFromZero);
}
