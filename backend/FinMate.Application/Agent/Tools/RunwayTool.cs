using System.Text.Json;

namespace FinMate.Application.Agent;

/// <summary>
/// Công cụ <c>runway</c>: "nếu thu nhập dừng, số dư hiện có đủ sống mấy tháng?" = <c>⌊tổng số dư / chi tiêu tháng cao (Q90)⌋</c>. Dùng chi tiêu CAO chứ không phải trung bình để không hứa quá tay.
/// Chưa có phân loại chi thiết yếu nên "chi tiêu" là tổng chi tháng (ruling ghi ở ledger Task 17); số tháng ghi ở <c>Text</c> của bằng chứng, không phải một số tiền.
/// </summary>
public sealed class RunwayTool(IFinancialDataSource source) : IAgentTool
{
    public ToolSpec Spec { get; } = new("runway", "Số dư hiện có đủ chi tiêu bao nhiêu tháng nếu thu nhập dừng lại (tính theo chi tiêu tháng cao).", []);

    public async Task<ToolResult> RunAsync(ToolContext context, JsonElement args, CancellationToken ct = default)
    {
        var inputs = await GoalInputs.LoadAsync(source, context, ct);
        if (inputs is null)
        {
            return ToolResult.Insufficient("history_too_short");
        }

        if (inputs.SpendQ90 <= 0)
        {
            return ToolResult.Insufficient("no_spending_history");
        }

        var months = Resilience.RunwayMonths(inputs.Balance, inputs.SpendQ90);
        var now = context.Now;
        return ToolResult.Ok(
        [
            inputs.Money("balance", "total_balance", inputs.Balance, EvidenceSource.Confirmed, now, "Tổng số dư các tài khoản", derived: false),
            inputs.Money("spend_q90", "spend_q90", inputs.SpendQ90, EvidenceSource.Estimated, now, "Chi tiêu tháng cao (phân vị 90)"),
            new Evidence("runway", "runway_months", null, $"Số dư đủ chi tiêu khoảng {months} tháng nếu thu nhập dừng (làm tròn xuống, theo chi tiêu tháng cao)", "months", EvidenceSource.Estimated, now,
                ConfidenceCalculator.Compute(EvidenceSource.Estimated, now, now, inputs.Completeness)),
        ]);
    }
}
