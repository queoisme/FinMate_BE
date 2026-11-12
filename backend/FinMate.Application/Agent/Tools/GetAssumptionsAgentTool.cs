using System.Text.Json;

namespace FinMate.Application.Agent;

/// <summary>Công cụ <c>get_assumptions</c>: tra một giả định kinh tế đã duyệt theo khóa. Không có thì trả "thiếu dữ liệu", không bao giờ trả số mặc định.</summary>
public sealed class GetAssumptionsAgentTool(GetAssumptionsTool assumptions) : IAgentTool, IRuntimeDescribedTool
{
    private const int MaxListedKeys = 30;

    public ToolSpec Spec { get; } = new("get_assumptions", "Tra giả định kinh tế đã duyệt (nguồn, ngày số liệu) theo khóa.", [new ToolArg("key", "string", true)]);

    /// <summary>Thêm các khóa đang có vào mô tả: LLM chỉ chọn trong danh sách này thay vì đoán tên khóa (đoán sai thì công cụ báo "thiếu giả định" dù đã có bản duyệt).</summary>
    public async Task<ToolSpec> GetSpecAsync(CancellationToken ct = default)
    {
        var keys = (await assumptions.ApprovedKeysAsync(ct)).Take(MaxListedKeys).ToArray();
        var suffix = keys.Length == 0 ? " Hiện chưa có khóa nào được duyệt." : $" Chỉ dùng một trong các khóa đang có: {string.Join(", ", keys)}.";
        return Spec with { Description = Spec.Description + suffix };
    }

    public async Task<ToolResult> RunAsync(ToolContext context, JsonElement args, CancellationToken ct = default)
    {
        var found = await assumptions.GetApprovedAsync(args.GetProperty("key").GetString()!, context.Now, ct);
        return found is null ? ToolResult.Insufficient("assumption_missing") : ToolResult.Ok([found.Evidence]);
    }
}
