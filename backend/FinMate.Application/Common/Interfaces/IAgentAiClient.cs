using System.Text.Json;
using FinMate.Application.Agent;

namespace FinMate.Application.Common.Interfaces;

public sealed record AgentGateResult(bool InScope, string ReasonCode, string? RefusalText);

/// <summary>Công cụ nào bị từ chối vì sao ("invalid_argument:price_vnd"): bộ lập kế hoạch đọc để sửa tham số ở lần lập lại.</summary>
public sealed record AgentPlanFeedback(string Tool, string Code);

public sealed record AgentPlanRequest(string Text, string Segment, IReadOnlyList<ToolSpec> Tools, int MaxSteps, IReadOnlyList<AgentPlanFeedback>? Feedback = null);

public sealed record AgentPlanStep(string Tool, JsonElement Args);

public sealed record AgentUsage(int InputTokens, int OutputTokens, string Provider, string Model);

/// <param name="Clarify">Có thì dừng và hỏi lại người dùng đúng câu này; không chạy công cụ.</param>
public sealed record AgentPlanResult(string Intent, IReadOnlyList<AgentPlanStep> Steps, string? Clarify, AgentUsage? Usage);

public sealed record AgentClaim(string Text, IReadOnlyList<string> EvidenceIds);

public sealed record AgentComposeResult(IReadOnlyList<AgentClaim> Claims, bool UsedTemplate, bool NoData, AgentUsage? Usage);

/// <summary>
/// Ba lời gọi của vòng agent sang ai-service (cổng phạm vi, lập kế hoạch, soạn câu). Không có lời gọi nào mang <c>userId</c> hay mã băm của nó. Mọi lỗi mạng/HTTP/định dạng
/// đều là <see cref="Exceptions.AIServiceUnavailableException"/>: bộ điều phối quyết định rơi về câu mẫu hay trả 503, không để lỗi lạ trôi lên.
/// </summary>
public interface IAgentAiClient
{
    Task<AgentGateResult> GateAsync(string text, CancellationToken ct = default);

    Task<AgentPlanResult> PlanAsync(AgentPlanRequest request, CancellationToken ct = default);

    /// <param name="skipVerifier">Chỉ có hiệu lực khi ai-service chạy ở chế độ đánh giá (AGENT_EVAL_ENABLED); production bỏ qua cờ này.</param>
    Task<AgentComposeResult> ComposeAsync(string question, IReadOnlyList<Evidence> evidence, bool skipVerifier = false, CancellationToken ct = default);
}
