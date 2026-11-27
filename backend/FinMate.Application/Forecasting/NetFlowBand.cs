using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <summary>
/// Phase 97 tầng 2 (phương án B2, chọn ở Task 8): dải số dư từ phân phối của TỔNG dòng tiền linh hoạt ròng (thu linh hoạt − chi linh hoạt) trong L ngày tới,
/// thay cho việc cộng dồn phân vị 25/75 của từng ngày (cộng phân vị làm dải rộng gấp 2–4 lần dải thật ở tầm dài, chẩn đoán C4).
/// <list type="bullet">
/// <item>Ngày gần như độc lập nên phương sai của tổng tăng theo L: σ_L² = σ²·L + (σ²/n)·L². Số hạng sau là sai số của chính mức trung bình ước lượng từ n ngày.</item>
/// <item>Dải = đường dự kiến ± z·σ_L, z = 0,6745 (P25/P75 của phân phối chuẩn; tổng của nhiều ngày đuôi nặng tiến về chuẩn). Không có tham số dò.</item>
/// <item>σ² là phương sai mẫu của phần dư ngày (dòng tiền ròng trừ trung bình của thứ đó), cùng chuỗi và cùng bộ lọc với đường dự kiến
/// (<see cref="SpendingBaselineCalculator"/>, <see cref="IrregularIncomeBaselineCalculator"/>), nên bất định của thu không đều nằm sẵn trong σ.</item>
/// </list>
/// Chỉ dùng khi đủ <c>minHistoryDays</c> ngày có giao dịch; dưới ngưỡng engine giữ các đường theo ngày của tầng 1.
/// </summary>
public sealed record NetFlowBand(double DailyVariance, int HistoryDays)
{
    /// <summary>P25/P75 của phân phối chuẩn.</summary>
    public const double Z = 0.6744897501960817;

    private const int DaysPerWeek = 7;

    /// <summary>Nửa độ rộng của dải cho tổng <paramref name="days"/> ngày.</summary>
    public long HalfWidth(int days)
        => (long)Math.Round(Z * Math.Sqrt(DailyVariance * days + DailyVariance / HistoryDays * days * days), MidpointRounding.AwayFromZero);

    /// <param name="history">Dữ liệu thói quen trong cửa sổ lịch sử, KHÔNG gồm hôm nay.</param>
    /// <param name="excludedItems">Khoản định kỳ đang theo dõi + vừa hoàn thành, như hai đường dự kiến.</param>
    public static NetFlowBand? From(IReadOnlyList<ForecastTransaction> history, IReadOnlyList<RecurringItemView> excludedItems, DateOnly today, ForecastRules rules)
    {
        var series = Series(history, excludedItems, today, rules);
        return series is null ? null : FromSeries(series);
    }

    /// <summary>Dòng tiền linh hoạt ròng theo ngày, từ ngày đầu của lịch sử tới hôm qua, ngày không có = 0; null khi chưa đủ lịch sử.</summary>
    public static IReadOnlyList<(DateOnly Date, double Amount)>? Series(
        IReadOnlyList<ForecastTransaction> history, IReadOnlyList<RecurringItemView> excludedItems, DateOnly today, ForecastRules rules)
    {
        if (history.Count == 0 || history.Select(t => t.Date).Distinct().Count() < rules.MinHistoryDays)
        {
            return null;
        }

        var recurring = RecurringPaymentFilter.For(history, excludedItems, rules);
        var byDay = history
            .Where(t => !t.ExcludedFromForecast && !recurring.IsPayment(t)
                        && (t.Type == TransactionType.Debit || (t.Type == TransactionType.Credit && t.Relation is null && !t.IsLoan)))
            .GroupBy(t => t.Date)
            .ToDictionary(g => g.Key, g => (double)g.Sum(t => t.Type == TransactionType.Credit ? t.AmountCents : -t.AmountCents));
        var series = new List<(DateOnly, double)>();
        for (var d = history.Min(t => t.Date); d < today; d = d.AddDays(1))
        {
            series.Add((d, byDay.GetValueOrDefault(d)));
        }

        return series;
    }

    /// <summary>σ² từ phần dư quanh trung bình của từng thứ (thứ dưới <see cref="DailyLevels.MinSamplesPerWeekday"/> mẫu dùng mọi ngày); null khi dưới 2 ngày.</summary>
    public static NetFlowBand? FromSeries(IReadOnlyList<(DateOnly Date, double Amount)> series)
    {
        var n = series.Count;
        if (n < 2)
        {
            return null;
        }

        var all = series.Select(s => s.Amount).ToArray();
        var means = new double[DaysPerWeek];
        for (var dow = 0; dow < DaysPerWeek; dow++)
        {
            var samples = series.Where(s => (int)s.Date.DayOfWeek == dow).Select(s => s.Amount).ToArray();
            means[dow] = (samples.Length >= DailyLevels.MinSamplesPerWeekday ? samples : all).Average();
        }

        var residuals = series.Select(s => s.Amount - means[(int)s.Date.DayOfWeek]).ToArray();
        var mean = residuals.Average();
        return new NetFlowBand(residuals.Sum(r => (r - mean) * (r - mean)) / (n - 1), n);
    }
}
