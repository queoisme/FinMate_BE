using System.Text.Json;

namespace FinMate.Application.Agent;

/// <summary>
/// Công cụ <c>stress_test</c>: "nếu thu nhập giảm X% trong N tháng thì sao?". Thu nhập an toàn = Q10 với nhóm không ổn định, trung vị với nhóm ổn định (<see cref="Resilience.SafeIncome"/>);
/// trừ chi tiêu điển hình rồi cộng dồn N tháng. Kết luận: <b>feasible</b> = mỗi tháng vẫn không âm; <b>tight</b> = âm nhưng số dư còn đủ qua N tháng; <b>infeasible</b> = số dư hết trong N tháng.
/// </summary>
public sealed class StressTestTool(IFinancialDataSource source) : IAgentTool
{
    public ToolSpec Spec { get; } = new(
        "stress_test",
        "Giả sử thu nhập giảm income_drop_percent phần trăm trong months tháng: số dư còn lại và bao lâu thì hết tiền.",
        [
            new ToolArg("months", "int", true, 1, 120),
            new ToolArg("income_drop_percent", "int", true, 0, 100),
        ]);

    public async Task<ToolResult> RunAsync(ToolContext context, JsonElement args, CancellationToken ct = default)
    {
        var months = (int)args.GetProperty("months").GetInt64();
        var drop = (int)args.GetProperty("income_drop_percent").GetInt64();

        var inputs = await GoalInputs.LoadAsync(source, context, ct);
        if (inputs is null)
        {
            return ToolResult.Insufficient("history_too_short");
        }

        var result = Resilience.StressTest(inputs.Profile, inputs.SpendQ50, inputs.Balance, months, drop);
        var now = context.Now;
        var verdict = result.MonthlyCapacityVnd >= 0 ? "feasible" : result.ProjectedBalanceVnd >= 0 ? "tight" : "infeasible";
        var runOut = result.MonthsUntilEmpty is { } until ? $"; hết tiền sau khoảng {until} tháng" : string.Empty;

        return ToolResult.Ok(
        [
            inputs.Money("balance", "total_balance", inputs.Balance, EvidenceSource.Confirmed, now, "Tổng số dư các tài khoản", derived: false),
            inputs.Money("safe_income", "safe_income", result.SafeIncomeVnd, EvidenceSource.Estimated, now, inputs.Profile.Segment == IncomeSegment.Variable ? "Thu nhập an toàn (tháng thấp, phân vị 10)" : "Thu nhập an toàn (trung vị)"),
            inputs.Money("stressed_income", "stressed_income", result.StressedIncomeVnd, EvidenceSource.Estimated, now, $"Thu nhập sau khi giảm {drop}%"),
            inputs.Money("spend_q50", "spend_q50", inputs.SpendQ50, EvidenceSource.Estimated, now, "Chi tiêu tháng điển hình (trung vị)"),
            inputs.Money("stress_capacity", "stress_capacity", result.MonthlyCapacityVnd, EvidenceSource.Estimated, now, "Dư (hoặc thiếu) mỗi tháng trong kịch bản giảm thu nhập"),
            inputs.Money("stress_balance", "stress_balance", result.ProjectedBalanceVnd, EvidenceSource.Estimated, now, $"Số dư sau {months} tháng trong kịch bản giảm thu nhập"),
            new Evidence("verdict", "stress_verdict", null, $"verdict: {verdict} (giảm {drop}% trong {months} tháng, nhóm thu nhập {inputs.Profile.Segment.ToString().ToLowerInvariant()}{runOut})", "label",
                EvidenceSource.Estimated, now, ConfidenceCalculator.Compute(EvidenceSource.Estimated, now, now, inputs.Completeness)),
        ]);
    }
}
