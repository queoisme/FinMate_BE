using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <summary>
/// Mức thu không định kỳ mỗi ngày theo thứ trong tuần (Phase 97): đường dự kiến, thận trọng, lạc quan. Chỉ số mảng là <see cref="DayOfWeek"/>.
/// <see cref="SpendingBaseline.BasisHistory"/> / <see cref="SpendingBaseline.BasisAverage"/> / <see cref="SpendingBaseline.BasisNone"/> như chi linh hoạt.
/// </summary>
public sealed record IncomeBaseline(long[] Expected, long[] Conservative, long[] Optimistic, string Basis)
{
    public static IncomeBaseline None { get; } = new(new long[7], new long[7], new long[7], SpendingBaseline.BasisNone);

    public long ExpectedOn(DateOnly date) => Expected[(int)date.DayOfWeek];
    public long ConservativeOn(DateOnly date) => Conservative[(int)date.DayOfWeek];
    public long OptimisticOn(DateOnly date) => Optimistic[(int)date.DayOfWeek];
}

/// <summary>
/// Thu không định kỳ cho đường số dư (Phase 97, vấn đề 1 của backtest Phase 96: trước đây engine chỉ chiếu khoản thu ĐỊNH KỲ, nên người thu không đều luôn
/// bị dự báo thấp). Hàm thuần, đối xứng với <see cref="SpendingBaselineCalculator"/>:
/// <list type="bullet">
/// <item>Nguồn là dữ liệu thói quen (Q16 giữ nguyên: không vay/mượn, không chuyển khoản hay quỹ nhóm, không khoản hoàn); vẫn tự loại lại cho chắc.</item>
/// <item>Loại khoản thu khớp khoản thu định kỳ đang theo dõi hoặc vừa hoàn thành (engine đã đặt chúng vào ngày đến hạn — tính thêm ở đây là cộng hai lần),
/// và khoản user đánh dấu loại khỏi dự báo.</item>
/// <item>Đủ <c>minHistoryDays</c> ngày có giao dịch: trung bình theo thứ (<see cref="DailyLevels"/>, phương án F1 chọn ở Task 1). Thu dồn vào vài ngày thì trung vị
/// gần như luôn 0, nên phải là trung bình.</item>
/// <item>Đường thận trọng = dự kiến × min(1, P25 / trung bình của các tháng trọn vẹn) từ <see cref="IncomeEstimator.Irregular"/> (D3); chưa đủ 2 tháng thì 0.
/// Đường lạc quan = dự kiến. Tầng 2 sẽ thay cả ba bằng dải của tổng.</item>
/// <item>Ít lịch sử (D4): mức phẳng = tổng thu / số ngày đã có, đường thận trọng 0. Không có dữ liệu: 0.</item>
/// </list>
/// </summary>
public static class IrregularIncomeBaselineCalculator
{
    private const int DaysPerWeek = 7;

    /// <param name="history">Dữ liệu thói quen trong cửa sổ lịch sử, KHÔNG gồm hôm nay.</param>
    /// <param name="excludedItems">Khoản định kỳ đang theo dõi + vừa hoàn thành (D5 Phase 92), để loại khoản thu khớp chúng.</param>
    /// <param name="monthly">Ước lượng theo tháng trọn vẹn (phân vị 25 / trung bình); null khi chưa đủ 2 tháng.</param>
    public static IncomeBaseline Compute(
        IReadOnlyList<ForecastTransaction> history, IReadOnlyList<RecurringItemView> excludedItems, DateOnly today, ForecastRules rules, IrregularIncomeDto? monthly)
    {
        if (history.Count == 0)
        {
            return IncomeBaseline.None;
        }

        var firstDay = history.Min(t => t.Date);
        var byDay = history
            .Where(t => t.Type == TransactionType.Credit && !t.ExcludedFromForecast && t.Relation is null && !t.IsLoan
                        && !RecurringMatcher.MatchesAny(t, excludedItems, rules))
            .GroupBy(t => t.Date)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.AmountCents));

        if (history.Select(t => t.Date).Distinct().Count() < rules.MinHistoryDays)
        {
            var span = Math.Max(1, today.DayNumber - firstDay.DayNumber);
            var flat = Enumerable.Repeat(byDay.Values.Sum() / span, DaysPerWeek).ToArray();
            return new IncomeBaseline(flat, new long[DaysPerWeek], flat.ToArray(), SpendingBaseline.BasisAverage);
        }

        var series = new List<(DateOnly, double)>();
        for (var d = firstDay; d < today; d = d.AddDays(1))
        {
            series.Add((d, byDay.GetValueOrDefault(d)));
        }

        var expected = DailyLevels.From(series).Expected;
        var ratio = monthly is { AverageMonthlyCents: > 0 } m ? Math.Min(1.0, (double)m.ConservativeMonthlyCents / m.AverageMonthlyCents) : 0;
        var conservative = expected.Select(e => (long)Math.Round(e * ratio, MidpointRounding.AwayFromZero)).ToArray();
        return new IncomeBaseline(expected, conservative, expected.ToArray(), SpendingBaseline.BasisHistory);
    }
}
