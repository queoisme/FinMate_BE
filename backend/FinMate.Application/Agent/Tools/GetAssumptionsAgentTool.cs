using System.Text.Json;

namespace FinMate.Application.Agent;

/// <summary>Công cụ <c>get_assumptions</c>: tra một giả định kinh tế đã duyệt theo khóa. Không có thì trả "thiếu dữ liệu", không bao giờ trả số mặc định.</summary>
public sealed class GetAssumptionsAgentTool(GetAssumptionsTool assumptions) : IAgentTool
{
    public ToolSpec Spec { get; } = new("get_assumptions", "Tra giả định kinh tế đã duyệt (nguồn, ngày số liệu) theo khóa.", [new ToolArg("key", "string", true)]);

    public async Task<ToolResult> RunAsync(ToolContext context, JsonElement args, CancellationToken ct = default)
    {
        var found = await assumptions.GetApprovedAsync(args.GetProperty("key").GetString()!, context.Now, ct);
        return found is null ? ToolResult.Insufficient("assumption_missing") : ToolResult.Ok([found.Evidence]);
    }
}
