using FinMate.Application.Reports;

namespace FinMate.Application.Forecasting;

/// <summary>Kết quả dự báo mục tiêu tiết kiệm. Mã trạng thái là chuỗi ổn định cho app.</summary>
public static class GoalForecastStatus
{
    /// <summary>Đã tích lũy đủ.</summary>
    public const string Reached = "reached";

    /// <summary>Tốc độ góp = 0: không thể đạt với thói quen hiện tại.</summary>
    public const string Unreachable = "unreachable";

    /// <summary>Không có hạn chót: chỉ có ngày dự kiến.</summary>
    public const string NoDeadline = "no_deadline";

    /// <summary>Kịp hạn chót kể cả ở tốc độ thận trọng (BR-08.3).</summary>
    public const string OnTrack = "on_track";

    /// <summary>Không chắc kịp hạn: kèm mức cần góp mỗi tháng.</summary>
    public const string Behind = "behind";
}

/// <param name="AverageMonthlyCents">BR-08.1, mức trung bình — hoặc mức user đang thử.</param>
/// <param name="ConservativeMonthlyCents">BR-08.1, mức thận trọng (phân vị 25) — hoặc mức user đang thử.</param>
/// <param name="LikelyDate">BR-08.2: ngày đạt theo tốc độ trung bình; null nếu tốc độ 0.</param>
/// <param name="ConservativeDate">BR-08.2: ngày đạt theo tốc độ thận trọng; null nếu tốc độ 0.</param>
/// <param name="RequiredMonthlyCents">Mức cần góp mỗi tháng để kịp hạn chót; chỉ có khi có hạn chót và chưa chắc kịp.</param>
public sealed record GoalForecast(
    string Status,
    long RemainingCents,
    long AverageMonthlyCents,
    long ConservativeMonthlyCents,
    int MonthsOfData,
    bool IsScenario,
    DateOnly? LikelyDate,
    DateOnly? ConservativeDate,
    long? RequiredMonthlyCents);

/// <summary>
/// Ngày đạt mục tiêu tiết kiệm (Flow 6, trang 4). Hàm thuần. Tốc độ góp lấy từ các khoản góp vào chính mục tiêu theo tháng (giờ VN); tháng không góp tính 0.
/// "Mô phỏng nhiều kịch bản" được rút về hai kịch bản có ý nghĩa: trung bình (khả năng cao) và thận trọng; quyết định kịp hạn theo kịch bản thận trọng.
/// </summary>
public static class GoalForecastCalculator
{
    private const double DaysPerMonth = 30.4375;
    private const double ConservativeQuantile = 0.25;

    /// <summary>Quá chừng này tháng thì coi như không có ngày đạt (tốc độ quá nhỏ so với phần còn thiếu).</summary>
    private const int MaxMonths = 1200;

    /// <param name="monthlyTotals">Tổng góp mỗi tháng, mỗi tháng đúng một phần tử (kể cả 0), tháng cũ trước.</param>
    /// <param name="scenarioMonthlyCents">Mức góp hằng tháng user đang thử; null = dùng lịch sử.</param>
    public static GoalForecast Compute(
        long targetCents,
        long savedCents,
        DateOnly? deadline,
        IReadOnlyList<long> monthlyTotals,
        DateOnly today,
        long? scenarioMonthlyCents)
    {
        var remaining = Math.Max(0, targetCents - savedCents);
        var (average, conservative) = scenarioMonthlyCents is { } scenario
            ? (scenario, scenario)
            : Rates(monthlyTotals);
        if (remaining == 0)
        {
            return new GoalForecast(GoalForecastStatus.Reached, 0, average, conservative, monthlyTotals.Count, scenarioMonthlyCents is not null, today, today, null);
        }

        var likely = ReachDate(remaining, average, today);
        var safe = ReachDate(remaining, conservative, today);
        var status = average <= 0 ? GoalForecastStatus.Unreachable
            : deadline is null ? GoalForecastStatus.NoDeadline
            : safe is { } d && d <= deadline.Value ? GoalForecastStatus.OnTrack
            : GoalForecastStatus.Behind;
        long? required = deadline is { } due && status is GoalForecastStatus.Behind or GoalForecastStatus.Unreachable
            ? RequiredMonthly(remaining, today, due)
            : null;
        return new GoalForecast(status, remaining, average, conservative, monthlyTotals.Count, scenarioMonthlyCents is not null, likely, safe, required);
    }

    /// <summary>Phần còn thiếu chia cho số tháng còn lại tới hạn chót (tối thiểu 1 tháng), làm tròn lên.</summary>
    public static long RequiredMonthly(long remaining, DateOnly today, DateOnly deadline)
    {
        var months = Math.Max(1.0, (deadline.DayNumber - today.DayNumber) / DaysPerMonth);
        return (long)Math.Ceiling(remaining / months);
    }

    private static (long Average, long Conservative) Rates(IReadOnlyList<long> monthlyTotals)
    {
        if (monthlyTotals.Count == 0)
        {
            return (0, 0);
        }

        var values = monthlyTotals.Select(v => (double)v).ToArray();
        return (
            (long)Math.Round(values.Average(), MidpointRounding.AwayFromZero),
            (long)Math.Round(Statistics.Quantile(values, ConservativeQuantile), MidpointRounding.AwayFromZero));
    }

    private static DateOnly? ReachDate(long remaining, long monthly, DateOnly today)
    {
        if (monthly <= 0)
        {
            return null;
        }

        var months = (double)remaining / monthly;
        return months > MaxMonths ? null : today.AddDays((int)Math.Ceiling(months * DaysPerMonth));
    }
}
