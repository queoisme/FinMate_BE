using FinMate.Application.Reports;

namespace FinMate.Application.Agent;

/// <summary>Đầu vào chung của các công cụ dựa trên dải thu/chi (stress_test, runway, sensitivity): đọc CHỈ qua <see cref="IFinancialDataSource"/> với <c>userId</c> của lượt hỏi.</summary>
public sealed record GoalInputs(IncomeProfile Profile, long SpendQ10, long SpendQ50, long SpendQ90, long Balance, double Completeness)
{
    /// <returns>null nếu dưới <see cref="AgentLimits.MinHistoryMonths"/> tháng lịch sử (không dựng dải, công cụ trả "chưa đủ dữ liệu").</returns>
    public static async Task<GoalInputs?> LoadAsync(IFinancialDataSource source, ToolContext context, CancellationToken ct)
    {
        var series = await source.GetMonthlySeriesAsync(context.UserId, AgentLimits.HistoryMonths, context.Now, ct);
        var profile = IntervalForecaster.BuildProfile(series.IncomeVnd, await source.GetIncomeSegmentOverrideAsync(context.UserId, ct));
        if (profile.MonthsOfHistory < AgentLimits.MinHistoryMonths)
        {
            return null;
        }

        var spend = series.SpendVnd.OrderBy(v => v).ToArray();
        return new GoalInputs(
            profile,
            Statistics.Quantile(spend, 0.1), Statistics.Quantile(spend, 0.5), Statistics.Quantile(spend, 0.9),
            await source.GetTotalBalanceAsync(context.UserId, ct),
            Math.Min(1.0, profile.MonthsOfHistory / (double)AgentLimits.HistoryMonths));
    }

    public Evidence Money(string id, string key, long value, EvidenceSource source, DateTimeOffset now, string text, bool derived = true)
        => new(id, key, value, text, "VND", source, now, ConfidenceCalculator.Compute(source, now, now, derived ? Completeness : 1.0));
}
