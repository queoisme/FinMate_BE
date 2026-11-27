using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;

namespace FinMate.Backtest.Diagnostics;

/// <summary>
/// Phase 97, Task 8 (D7): hai cách dựng dải số dư từ phân phối của TỔNG dòng tiền linh hoạt ròng trong L = h + 1 ngày (§3 của kế hoạch). Task 8 đo trong harness
/// khi engine còn dải tầng 1; từ Task 9 engine dùng B2 (<see cref="NetFlowBand"/>), nên cột "dải của engine" trùng B2 và B2 ở đây gọi thẳng production. Chuỗi ngày là thu linh hoạt − chi linh hoạt, cùng bộ lọc với production (<see cref="RecurringPaymentFilter"/>), ngày không có = 0.
/// Hai đầu dải là độ lệch so với đường dự kiến, nên dải luôn bao quanh tâm đã sửa ở tầng 1.
/// </summary>
public static class SumBands
{
    public const string Current = "dải của engine";
    public const string B1 = "B1 phân vị tổng cuộn";
    public const string B2 = "B2 chuẩn (σ²L + σ²L²/n)";

    public static readonly IReadOnlyList<string> Candidates = [B1, B2];

    /// <summary>Cùng chuỗi với production (<see cref="NetFlowBand.Series"/>); null khi chưa đủ <c>minHistoryDays</c>.</summary>
    public static IReadOnlyList<(DateOnly Date, double Amount)>? NetSeries(
        IReadOnlyList<ForecastTransaction> history, IReadOnlyList<RecurringItemView> items, DateOnly origin, ForecastRules rules)
        => NetFlowBand.Series(history, items, origin, rules);

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

    /// <summary>B2: ± z·σ_L của production (<see cref="NetFlowBand"/>, Task 9). Null khi dưới 2 ngày.</summary>
    public static (double Low, double High)? Normal(IReadOnlyList<(DateOnly Date, double Amount)> series, int length)
        => NetFlowBand.FromSeries(series) is { } band ? (-band.HalfWidth(length), band.HalfWidth(length)) : null;

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
