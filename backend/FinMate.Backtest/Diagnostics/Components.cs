using FinMate.Application.Forecasting;
using FinMate.Backtest.Generation;
using FinMate.Domain.Enums;

namespace FinMate.Backtest.Diagnostics;

/// <summary>Các thành phần của thay đổi số dư (chẩn đoán, Phase 96). Tổng năm thành phần = số dư cuối − số dư lúc dự báo.</summary>
public enum Component
{
    /// <summary>Thu từ khoản thu định kỳ (lương, trợ cấp).</summary>
    IncomeRecurring,

    /// <summary>Thu không định kỳ: dự án, bán hàng, làm thêm dao động, thưởng/lì xì của kịch bản.</summary>
    IncomeIrregular,

    /// <summary>Chi cho khoản định kỳ của đáp án, gồm gửi tiết kiệm sang ví bị loại và góp quỹ hằng tháng.</summary>
    RecurringOutflow,

    /// <summary>Chi linh hoạt (kể cả khoản dao động như nhập hàng) trừ phần được hoàn.</summary>
    Flexible,

    /// <summary>Vay / trả nợ / chuyển vượt ranh giới không định kỳ.</summary>
    LoanTransfer,
}

/// <param name="Values">Đóng góp có dấu của từng thành phần vào thay đổi số dư (dương = làm số dư tăng).</param>
public sealed record ComponentFlows(IReadOnlyDictionary<Component, long> Values)
{
    public long Total => Values.Values.Sum();

    public long this[Component c] => Values.GetValueOrDefault(c);
}

/// <summary>
/// Tách dòng tiền thực tế và dự báo thành các thành phần. Thực tế đọc nhãn đáp án của từng giao dịch; dự báo đọc loại sự kiện của engine
/// (<see cref="CashflowEventKinds"/>) và chi linh hoạt từng ngày. Hàm thuần.
/// </summary>
public static class Components
{
    public static readonly IReadOnlyList<Component> All = Enum.GetValues<Component>();

    /// <param name="raw">Giao dịch thô trong [T, T+h] (như <c>GetRawTransactionsAsync</c>).</param>
    /// <param name="boundary">Chuyển khoản vượt ranh giới trong cùng đoạn.</param>
    public static ComponentFlows Actual(
        IEnumerable<ForecastTransaction> raw, IEnumerable<ForecastTransaction> boundary, IReadOnlyDictionary<Guid, SynthTransaction> truth)
    {
        var values = All.ToDictionary(c => c, _ => 0L);
        foreach (var row in raw)
        {
            var t = truth[row.Id];
            var signed = row.Type == TransactionType.Credit ? row.AmountCents : -row.AmountCents;
            values[OfRaw(t, row.Type)] += signed;
        }

        foreach (var row in boundary)
        {
            var signed = row.Type == TransactionType.Credit ? row.AmountCents : -row.AmountCents;
            values[truth[row.Id].RecurringName is null ? Component.LoanTransfer : Component.RecurringOutflow] += signed;
        }

        return new ComponentFlows(values);
    }

    /// <summary>Dự báo tới hết ngày thứ <paramref name="horizon"/> trên đường "dự kiến" (không gồm sự kiện chỉ thuộc đường lạc quan).</summary>
    public static ComponentFlows Predicted(CashflowForecastDto forecast, int horizon)
    {
        var values = All.ToDictionary(c => c, _ => 0L);
        foreach (var day in forecast.Days.Take(horizon + 1))
        {
            values[Component.Flexible] -= day.FlexibleSpendCents;
            values[Component.IncomeIrregular] += day.IrregularIncomeCents;   // Phase 97: engine chiếu thu không định kỳ
            foreach (var e in day.Events.Where(e => !e.OptimisticOnly))
            {
                values[OfEvent(e.Kind)] += e.AmountCents;
            }
        }

        return new ComponentFlows(values);
    }

    private static Component OfRaw(SynthTransaction t, TransactionType type) => t.Label switch
    {
        TxLabel.Recurring => type == TransactionType.Credit ? Component.IncomeRecurring : Component.RecurringOutflow,
        TxLabel.Loan => Component.LoanTransfer,
        TxLabel.Refund => Component.Flexible,
        _ when type == TransactionType.Credit => Component.IncomeIrregular,
        _ => Component.Flexible,
    };

    private static Component OfEvent(string kind) => kind switch
    {
        CashflowEventKinds.RecurringInflow => Component.IncomeRecurring,
        CashflowEventKinds.RecurringOutflow => Component.RecurringOutflow,
        CashflowEventKinds.LoanOutflow or CashflowEventKinds.LoanInflow => Component.LoanTransfer,
        _ => Component.IncomeIrregular,   // kịch bản giả định của người dùng; không có trong backtest
    };
}

/// <summary>
/// H2: dựng lại mức chi linh hoạt mỗi thứ trong tuần theo ba cách trên CÙNG chuỗi ngày mà <see cref="SpendingBaselineCalculator"/> dùng
/// (Debit, không loại khỏi dự báo, không khớp khoản định kỳ, ngày không chi = 0, thứ nào dưới 3 mẫu thì dùng mọi ngày).
/// (a) trung vị sau khi cắt ngoại lai trên — phải trùng production; (b) trung vị không cắt; (c) trung bình không cắt.
/// </summary>
public static class FlexVariants
{
    private const int DaysPerWeek = 7;
    private const int MinSamplesPerWeekday = 3;

    public sealed record Levels(long[] TrimmedMedian, long[] Median, long[] Mean, IReadOnlyList<double> Series);

    public static Levels? Compute(IReadOnlyList<ForecastTransaction> history, IReadOnlyList<RecurringItemView> items, DateOnly origin, ForecastRules rules)
    {
        if (history.Select(t => t.Date).Distinct().Count() < rules.MinHistoryDays)
        {
            return null;   // nhánh thiếu lịch sử (ngân sách / trung bình), không thuộc H2
        }

        var recurring = RecurringPaymentFilter.For(history, items, rules);
        var byDay = history
            .Where(t => t.Type == TransactionType.Debit && !t.ExcludedFromForecast && !recurring.IsPayment(t))
            .GroupBy(t => t.Date)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.AmountCents));
        var series = new List<(DateOnly Date, double Amount)>();
        for (var d = history.Min(t => t.Date); d < origin; d = d.AddDays(1))
        {
            series.Add((d, byDay.GetValueOrDefault(d)));
        }

        var all = series.Select(s => s.Amount).ToArray();
        var (trimmed, median, mean) = (new long[DaysPerWeek], new long[DaysPerWeek], new long[DaysPerWeek]);
        for (var dow = 0; dow < DaysPerWeek; dow++)
        {
            var samples = series.Where(s => (int)s.Date.DayOfWeek == dow).Select(s => s.Amount).ToArray();
            var values = samples.Length >= MinSamplesPerWeekday ? samples : all;
            trimmed[dow] = Round(Application.Reports.Statistics.Median(Application.Reports.Statistics.TrimUpperOutliers(values)));
            median[dow] = Round(Application.Reports.Statistics.Median(values));
            mean[dow] = Round(values.Average());
        }

        return new Levels(trimmed, median, mean, all);
    }

    public static long Over(long[] levels, DateOnly origin, int horizon)
        => Enumerable.Range(0, horizon + 1).Sum(i => levels[(int)origin.AddDays(i).DayOfWeek]);

    /// <summary>Dải P25–P75 của TỔNG h+1 ngày, lấy từ mọi cửa sổ trượt h+1 ngày trong lịch sử (để so với dải "cộng phân vị từng ngày").</summary>
    public static (long Low, long High)? SumQuantiles(IReadOnlyList<double> series, int horizon)
    {
        var length = horizon + 1;
        if (series.Count < length)
        {
            return null;
        }

        var sums = new List<double>();
        var running = series.Take(length).Sum();
        sums.Add(running);
        for (var i = length; i < series.Count; i++)
        {
            running += series[i] - series[i - length];
            sums.Add(running);
        }

        return (Round(Application.Reports.Statistics.Quantile(sums, 0.25)), Round(Application.Reports.Statistics.Quantile(sums, 0.75)));
    }

    private static long Round(double v) => (long)Math.Round(v, MidpointRounding.AwayFromZero);
}
