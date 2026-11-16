using System.Text.Json;
using FinMate.Application.Reports;

namespace FinMate.Application.Agent;

/// <summary>
/// Công cụ <c>forecast_goal</c>: "mua một thứ giá X trong N tháng được không?". Đọc dữ liệu CHỈ qua <see cref="IFinancialDataSource"/> với <c>userId</c> của lượt hỏi,
/// chạy <see cref="GoalSimulator"/>, rồi trả gói bằng chứng: mỗi số tiền có id, nguồn và độ tin cậy riêng để câu trả lời trích dẫn được.
/// Dưới <see cref="AgentLimits.MinHistoryMonths"/> tháng lịch sử thì không dựng dải, trả "chưa đủ dữ liệu".
/// </summary>
public sealed class ForecastGoalTool(IFinancialDataSource source, GetAssumptionsTool assumptions) : IAgentTool
{
    public ToolSpec Spec { get; } = new(
        "forecast_goal",
        "Dự báo bao lâu nữa đủ tiền mua một món giá price_vnd, trong horizon_months tháng, theo ba kịch bản.",
        [
            new ToolArg("price_vnd", "int", true, 1, AgentLimits.MaxPriceVnd),
            new ToolArg("horizon_months", "int", true, 1, AgentLimits.MaxSearchMonths),
            new ToolArg("growth_key", "string", false),
        ]);

    public async Task<ToolResult> RunAsync(ToolContext context, JsonElement args, CancellationToken ct = default)
    {
        var price = args.GetProperty("price_vnd").GetInt64();
        var horizon = (int)args.GetProperty("horizon_months").GetInt64();
        var growthKey = args.TryGetProperty("growth_key", out var key) ? key.GetString()! : AgentLimits.DefaultGrowthAssumptionKey;

        var series = await source.GetMonthlySeriesAsync(context.UserId, AgentLimits.HistoryMonths, context.Now, ct);
        var segmentOverride = await source.GetIncomeSegmentOverrideAsync(context.UserId, ct);
        var balance = await source.GetTotalBalanceAsync(context.UserId, ct);

        var profile = IntervalForecaster.BuildProfile(series.IncomeVnd, segmentOverride);
        if (profile.MonthsOfHistory < AgentLimits.MinHistoryMonths)
        {
            return ToolResult.Insufficient("history_too_short");
        }

        var spend = series.SpendVnd.OrderBy(v => v).ToArray();
        var (s10, s50, s90) = (Statistics.Quantile(spend, 0.1), Statistics.Quantile(spend, 0.5), Statistics.Quantile(spend, 0.9));
        var growth = await assumptions.GetApprovedAsync(growthKey, context.Now, ct);
        var simulation = GoalSimulator.Simulate(price, balance, profile, s10, s50, s90, growth?.RateBps ?? 0, horizon);

        var now = context.Now;
        var completeness = Math.Min(1.0, profile.MonthsOfHistory / (double)AgentLimits.HistoryMonths); // ít tháng hơn thì kém đầy đủ hơn
        Evidence Money(string id, string evidenceKey, long value, EvidenceSource evidenceSource, double fullness = 1.0, string? text = null)
            => new(id, evidenceKey, value, text, "VND", evidenceSource, now, ConfidenceCalculator.Compute(evidenceSource, now, now, fullness));

        var evidence = new List<Evidence>
        {
            Money("price", "goal_price", price, EvidenceSource.UserEntered, text: "Giá mục tiêu do người dùng nêu"),
            Money("balance", "total_balance", balance, EvidenceSource.Confirmed, text: "Tổng số dư các tài khoản"),
            Money("income_q10", "income_q10", profile.Q10, EvidenceSource.Estimated, completeness, "Thu nhập tháng thấp (phân vị 10)"),
            Money("income_q50", "income_q50", profile.Q50, EvidenceSource.Estimated, completeness, "Thu nhập tháng điển hình (trung vị)"),
            Money("income_q90", "income_q90", profile.Q90, EvidenceSource.Estimated, completeness, "Thu nhập tháng cao (phân vị 90)"),
            Money("spend_q10", "spend_q10", s10, EvidenceSource.Estimated, completeness, "Chi tiêu tháng thấp (phân vị 10)"),
            Money("spend_q50", "spend_q50", s50, EvidenceSource.Estimated, completeness, "Chi tiêu tháng điển hình (trung vị)"),
            Money("spend_q90", "spend_q90", s90, EvidenceSource.Estimated, completeness, "Chi tiêu tháng cao (phân vị 90)"),
            Money("price_at_horizon", "price_at_horizon", simulation.PriceAtHorizonVnd, EvidenceSource.Estimated, text: $"Giá mục tiêu sau {horizon} tháng theo giả định tăng giá"),
        };

        evidence.Add(growth?.Evidence ?? new Evidence(
            "price_growth", growthKey, null, $"Chưa có giả định tăng giá đã duyệt cho '{growthKey}': giữ giá không đổi.", "pct_per_year",
            EvidenceSource.Estimated, now, ConfidenceCalculator.Compute(EvidenceSource.Estimated, now, now, 0.5)));

        foreach (var scenario in simulation.Scenarios)
        {
            var reach = scenario.MonthsToReach is { } months ? $"đạt mục tiêu sau {months} tháng" : $"không đạt mục tiêu trong {AgentLimits.MaxSearchMonths} tháng";
            evidence.Add(Money($"scenario_{scenario.Name}", $"capacity_{scenario.Name}", scenario.MonthlyCapacityVnd, EvidenceSource.Estimated, completeness, $"Kịch bản {scenario.Name}: dư mỗi tháng; {reach}"));
            evidence.Add(Money($"shortfall_{scenario.Name}", $"shortfall_{scenario.Name}", scenario.ShortfallVnd, EvidenceSource.Estimated, completeness, $"Kịch bản {scenario.Name}: còn thiếu sau {horizon} tháng"));
        }

        evidence.Add(new Evidence(
            "verdict", "goal_verdict", null, $"verdict: {simulation.Verdict.ToString().ToLowerInvariant()} (horizon {horizon} tháng, nhóm thu nhập {profile.Segment.ToString().ToLowerInvariant()})",
            "label", EvidenceSource.Estimated, now, ConfidenceCalculator.Compute(EvidenceSource.Estimated, now, now, completeness)));

        return ToolResult.Ok(evidence);
    }
}
