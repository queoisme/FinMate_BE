using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;

namespace FinMate.Backtest.Evaluation;

/// <summary>
/// Mốc tham chiếu của D11 (đặc tả bộ sinh §9). Hàm thuần; mọi đầu vào đã cắt ở mốc (chỉ dữ liệu trước T). Không có tham số nào chọn theo kết quả:
/// cửa sổ 30 ngày của B-mean30 ghi sẵn trong đặc tả.
/// </summary>
public static class Baselines
{
    /// <summary>Tổng sự kiện của đường "dự kiến" từ ngày gốc tới hết ngày thứ <paramref name="horizon"/> (bỏ sự kiện chỉ thuộc đường lạc quan).</summary>
    public static long ExpectedEvents(CashflowForecastDto forecast, int horizon)
        => forecast.Days.Take(horizon + 1).SelectMany(d => d.Events).Where(e => !e.OptimisticOnly).Sum(e => e.AmountCents);

    /// <summary>Chi linh hoạt mô hình dự kiến từ ngày gốc tới hết ngày thứ <paramref name="horizon"/>.</summary>
    public static long ModelFlexible(CashflowForecastDto forecast, int horizon) => forecast.Days.Take(horizon + 1).Sum(d => d.FlexibleSpendCents);

    /// <summary>
    /// Chi linh hoạt trung bình mỗi ngày của tối đa <paramref name="windowDays"/> ngày trước mốc, cùng bộ lọc với <see cref="SpendingBaselineCalculator"/>
    /// (Debit, không bị loại khỏi dự báo, không khớp khoản định kỳ đang theo dõi). Người dùng mới chưa đủ cửa sổ thì chia cho số ngày đã có.
    /// </summary>
    public static double MeanDailyFlexible(
        IReadOnlyList<ForecastTransaction> habits, IReadOnlyList<RecurringItemView> items, ForecastRules rules, DateOnly origin, DateOnly firstDay, int windowDays)
    {
        var from = Max(origin.AddDays(-windowDays), firstDay);
        var days = origin.DayNumber - from.DayNumber;
        if (days <= 0)
        {
            return 0;
        }

        var total = habits
            .Where(t => t.Date >= from && t.Date < origin && t.Type == TransactionType.Debit && !t.ExcludedFromForecast && !RecurringMatcher.MatchesAny(t, items, rules))
            .Sum(t => t.AmountCents);
        return (double)total / days;
    }

    /// <summary>B-mean30: số dư tại mốc + sự kiện định kỳ của mô hình − trung bình chi linh hoạt × (h + 1) ngày.</summary>
    public static long Mean(long start, long expectedEvents, double meanDaily, int horizon) => start + expectedEvents - Round(meanDaily * (horizon + 1));

    /// <summary>
    /// B-naive: lặp lại dòng tiền ròng của h + 1 ngày ngay trước mốc. Người dùng mới chưa đủ ngày thì nhân theo tỉ lệ số ngày, để vẫn là "h + 1 ngày".
    /// </summary>
    public static long Naive(long start, long netFlowBefore, int daysAvailable, int horizon)
        => daysAvailable <= 0 ? start : start + Round((double)netFlowBefore * (horizon + 1) / daysAvailable);

    /// <summary>Số ngày dữ liệu có thể dùng trong <c>[mốc − (h + 1), mốc − 1]</c>.</summary>
    public static (DateOnly From, int Days) NaiveWindow(DateOnly origin, DateOnly firstDay, int horizon)
    {
        var from = Max(origin.AddDays(-(horizon + 1)), firstDay);
        return (from, Math.Max(0, origin.DayNumber - from.DayNumber));
    }

    public static long Round(double value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
}
