using System.Text.Json;

namespace FinMate.Application.Agent;

/// <summary>
/// Công cụ <c>sensitivity</c>: kết luận mục tiêu nhạy nhất với yếu tố nào? Với mỗi yếu tố (tăng giá, thu nhập, chi tiêu) tăng/giảm <c>delta_bps</c> điểm cơ bản (mặc định 100 = 1%), chạy lại kịch bản cơ sở
/// và xếp theo mức đổi của "số tháng đạt mục tiêu" (<see cref="Resilience.Sensitivity"/>). Số tháng nằm ở <c>Text</c> của bằng chứng (không phải số tiền); giả định tăng giá lấy từ bản đã duyệt.
/// </summary>
public sealed class SensitivityTool(IFinancialDataSource source, GetAssumptionsTool assumptions) : IAgentTool
{
    private const int DefaultDeltaBps = 100;

    public ToolSpec Spec { get; } = new(
        "sensitivity",
        "Yếu tố nào (tăng giá, thu nhập, chi tiêu) làm số tháng đạt mục tiêu price_vnd thay đổi nhiều nhất khi lệch delta_bps điểm cơ bản.",
        [
            new ToolArg("price_vnd", "int", true, 1, AgentLimits.MaxPriceVnd),
            new ToolArg("horizon_months", "int", true, 1, AgentLimits.MaxSearchMonths),
            new ToolArg("delta_bps", "int", false, 1, 2_000),
            new ToolArg("growth_key", "string", false),
        ]);

    public async Task<ToolResult> RunAsync(ToolContext context, JsonElement args, CancellationToken ct = default)
    {
        var price = args.GetProperty("price_vnd").GetInt64();
        var horizon = (int)args.GetProperty("horizon_months").GetInt64();
        var delta = args.TryGetProperty("delta_bps", out var d) && d.TryGetInt64(out var parsed) ? (int)parsed : DefaultDeltaBps;
        var growthKey = args.TryGetProperty("growth_key", out var key) ? key.GetString()! : AgentLimits.DefaultGrowthAssumptionKey;

        var inputs = await GoalInputs.LoadAsync(source, context, ct);
        if (inputs is null)
        {
            return ToolResult.Insufficient("history_too_short");
        }

        var growth = await assumptions.GetApprovedAsync(growthKey, context.Now, ct);
        var rows = Resilience.Sensitivity(price, inputs.Balance, inputs.Profile, inputs.SpendQ10, inputs.SpendQ50, inputs.SpendQ90, growth?.RateBps ?? 0, horizon, delta);

        var now = context.Now;
        var evidence = new List<Evidence>
        {
            inputs.Money("price", "goal_price", price, EvidenceSource.UserEntered, now, "Giá mục tiêu do người dùng nêu", derived: false),
            inputs.Money("balance", "total_balance", inputs.Balance, EvidenceSource.Confirmed, now, "Tổng số dư các tài khoản", derived: false),
        };
        if (growth is not null)
        {
            evidence.Add(growth.Evidence);
        }

        var rank = 0;
        foreach (var row in rows)
        {
            evidence.Add(new Evidence(
                $"sensitivity_{++rank}", $"sensitivity_{row.Driver}", null,
                $"{Label(row.Driver)} lệch ±{delta / 100},{delta % 100:00}%: số tháng đạt mục tiêu từ {Months(row.MonthsIfUp)} (khi tăng) đến {Months(row.MonthsIfDown)} (khi giảm), chênh {row.Swing} tháng; xếp hạng ảnh hưởng #{rank}",
                "months", EvidenceSource.Estimated, now, ConfidenceCalculator.Compute(EvidenceSource.Estimated, now, now, inputs.Completeness)));
        }

        return ToolResult.Ok(evidence);
    }

    private static string Label(string driver) => driver switch { "price_growth" => "Tốc độ tăng giá", "income" => "Thu nhập", _ => "Chi tiêu" };

    private static string Months(int? months) => months is { } m ? $"{m} tháng" : "không đạt";
}
