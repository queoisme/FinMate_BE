using System.Text.Json;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Agent;

/// <summary>
/// Công cụ <c>search_knowledge</c>: tra kho tri thức ĐÃ DUYỆT (phương pháp quản lý tiền, khái niệm, quy định) qua ai-service. Mỗi đoạn trả về là bằng chứng nguồn <c>Document</c> kèm tên nguồn và ngày cập nhật.
/// Không có đoạn nào, kho tắt, hoặc ai-service không dùng được thì trả "thiếu dữ liệu" — không bao giờ bịa tri thức.
/// </summary>
public sealed class SearchKnowledgeAgentTool(IAgentAiClient ai, AgentRunOptions options) : IAgentTool
{
    private const int TopK = 3;

    public ToolSpec Spec { get; } = new(
        "search_knowledge",
        "Tra tri thức tài chính đã được người duyệt (phương pháp quản lý tiền, khái niệm, quy định) theo một cụm từ khóa ngắn của câu hỏi.",
        [new ToolArg("query", "string", true, Max: 120)]);

    public async Task<ToolResult> RunAsync(ToolContext context, JsonElement args, CancellationToken ct = default)
    {
        if (!options.RagEnabled)
        {
            return ToolResult.Insufficient("knowledge_missing");
        }

        try
        {
            var found = await ai.SearchKnowledgeAsync(args.GetProperty("query").GetString()!, TopK, ct);
            return found.Count == 0 ? ToolResult.Insufficient("knowledge_missing") : ToolResult.Ok(found);
        }
        catch (AIServiceUnavailableException)
        {
            return ToolResult.Insufficient("knowledge_missing");
        }
    }
}
