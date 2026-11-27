using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;

namespace FinMate.Backtest.Diagnostics;

/// <summary>
/// Phase 97, Task 8 (D7): hai cách dựng dải số dư từ phân phối của TỔNG dòng tiền linh hoạt ròng trong L = h + 1 ngày (§3 của kế hoạch). Chỉ đo trong harness;
/// engine chưa đổi. Chuỗi ngày là thu linh hoạt − chi linh hoạt, cùng bộ lọc với production (<see cref="RecurringPaymentFilter"/>), ngày không có = 0.
/// Hai đầu dải là độ lệch so với đường dự kiến, nên dải luôn bao quanh tâm đã sửa ở tầng 1.
/// </summary>
public static class SumBands
{
    public const string Current = "dải hiện tại (tầng 1)";
    public const string B1 = "B1 phân vị tổng cuộn";
    public const string B2 = "B2 chuẩn (σ²L + σ²L²/n)";

    public static readonly IReadOnlyList<string> Candidates = [B1, B2];

    /// <summary>P25/P75 của phân phối chuẩn.</summary>
    public const double Z = 0.6744897501960817;

    /// <summary>Dòng tiền linh hoạt ròng theo ngày, từ ngày đầu của lịch sử tới hết ngày trước mốc; null khi chưa đủ <c>minHistoryDays</c> (nhánh thiếu lịch sử).</summary>
    public static IReadOnlyList<(DateOnly Date, double Amount)>? NetSeries(
        IReadOnlyList<ForecastTransaction> history, IReadOnlyList<RecurringItemView> items, DateOnly origin, ForecastRules rules)
    {
        if (history.Count == 0 || history.Select(t => t.Date).Distinct().Count() < rules.MinHistoryDays)
        {
            return null;
        }

        var recurring = RecurringPaymentFilter.For(history, items, rules);
        var byDay = history
            .Where(t => !t.ExcludedFromForecast && !recurring.IsPayment(t)
                        && (t.Type == TransactionType.Debit || (t.Type == TransactionType.Credit && t.Relation is null && !t.IsLoan)))
            .GroupBy(t => t.Date)
            .ToDictionary(g => g.Key, g => (double)g.Sum(t => t.Type == TransactionType.Credit ? t.AmountCents : -t.AmountCents));
        var series = new List<(DateOnly, double)>();
        for (var d = history.Min(t => t.Date); d < origin; d = d.AddDays(1))
        {
            series.Add((d, byDay.GetValueOrDefault(d)));
        }

        return series;
    }

    /// <summary>B1: phân vị 25/75 của mọi tổng trượt L ngày, trừ trung bình × L. Null khi lịch sử ngắn hơn L.</summary>
    public static (double Low, double High)? RollingQuantiles(IReadOnlyList<(DateOnly Date, double Amount)> series, int length)
    {
        if (series.Count < length)
        {
            return null;
        }

        var values = series.Select(s => s.Amount).ToArray();
        var centre = values.Average() * length;
        var sums = new List<double>();
        var running = values.Take(length).Sum();
        sums.Add(running);
        for (var i = length; i < values.Length; i++)
        {
            running += values[i] - values[i - length];
            sums.Add(running);
        }

        return (Application.Reports.Statistics.Quantile(sums, 0.25) - centre, Application.Reports.Statistics.Quantile(sums, 0.75) - centre);
    }

    /// <summary>
    /// B2: ± z·σ_L với σ_L² = σ²·L + (σ²/n)·L². σ² là phương sai mẫu của phần dư ngày (dòng tiền ròng trừ trung bình của thứ đó; thứ dưới 3 mẫu dùng mọi ngày),
    /// n là số ngày lịch sử. Số hạng đầu là bất định của các ngày tới, số hạng sau là sai số của chính mức trung bình ước lượng từ n ngày.
    /// </summary>
    public static (double Low, double High)? Normal(IReadOnlyList<(DateOnly Date, double Amount)> series, int length)
    {
        var n = series.Count;
        if (n < 2)
        {
            return null;
        }

        var all = series.Select(s => s.Amount).ToArray();
        var means = new double[7];
        for (var dow = 0; dow < 7; dow++)
        {
            var samples = series.Where(s => (int)s.Date.DayOfWeek == dow).Select(s => s.Amount).ToArray();
            means[dow] = (samples.Length >= DailyLevels.MinSamplesPerWeekday ? samples : all).Average();
        }

        var residuals = series.Select(s => s.Amount - means[(int)s.Date.DayOfWeek]).ToArray();
        var mean = residuals.Average();
        var variance = residuals.Sum(r => (r - mean) * (r - mean)) / (n - 1);
        var sigma = Math.Sqrt(variance * length + variance / n * length * length);
        return (-Z * sigma, Z * sigma);
    }

    /// <summary>Dải số dư tại tầm <paramref name="horizon"/> theo một phương án; null khi phương án không tính được (thiếu lịch sử).</summary>
    public static (long Low, long High)? At(string method, CashflowForecastDto forecast, int horizon, IReadOnlyList<(DateOnly Date, double Amount)>? series)
    {
        var day = forecast.Days[horizon];
        if (method == Current)
        {
            return (Math.Min(day.LowBalanceCents, day.HighBalanceCents), Math.Max(day.LowBalanceCents, day.HighBalanceCents));
        }

        if (series is null)
        {
            return null;
        }

        var offsets = method == B1 ? RollingQuantiles(series, horizon + 1) : Normal(series, horizon + 1);
        if (offsets is not { } o)
        {
            return null;
        }

        // Khoản cho vay đến hạn chỉ vào đường lạc quan (BR-DEBT-16), cộng sau khi tính dải.
        var optimistic = forecast.Days.Take(horizon + 1).SelectMany(d => d.Events).Where(e => e.OptimisticOnly).Sum(e => e.AmountCents);
        return (Round(day.ExpectedBalanceCents + o.Low), Round(day.ExpectedBalanceCents + o.High) + optimistic);
    }

    private static long Round(double v) => (long)Math.Round(v, MidpointRounding.AwayFromZero);
}
