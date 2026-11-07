using System.Text.Json;
using FinMate.Application.Agent;

namespace FinMate.Application.Common.Interfaces;

public sealed record AgentGateResult(bool InScope, string ReasonCode, string? RefusalText);

/// <summary>Công cụ nào bị từ chối vì sao ("invalid_argument:price_vnd"): bộ lập kế hoạch đọc để sửa tham số ở lần lập lại.</summary>
public sealed record AgentPlanFeedback(string Tool, string Code);

/// <summary>Một sở thích dài hạn do người dùng đặt (đưa vào lời nhắc dưới dạng DỮ LIỆU, không phải chỉ dẫn).</summary>
public sealed record AgentMemoryItem(string Key, string Value);

public sealed record AgentMemoryEntry(string Key, string Value, DateTimeOffset UpdatedAt);

/// <summary>Ngữ cảnh ngắn hạn của lượt trước: ý định và các "ô" có cấu trúc, không có văn bản thô.</summary>
public sealed record AgentPlanState(string? LastIntent, IReadOnlyDictionary<string, string> Slots);

public sealed record AgentPlanRequest(
    string Text,
    string Segment,
    IReadOnlyList<ToolSpec> Tools,
    int MaxSteps,
    IReadOnlyList<AgentPlanFeedback>? Feedback = null,
    AgentPlanState? State = null,
    IReadOnlyList<AgentMemoryItem>? Memory = null);

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

    /// <summary>Tìm đoạn tri thức ĐÃ DUYỆT và còn hiệu lực (nguồn <c>Document</c>, kèm tên nguồn và ngày). Mặc định không có kho: trả rỗng.</summary>
    Task<IReadOnlyList<Evidence>> SearchKnowledgeAsync(string query, int k, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Evidence>>([]);

    /// <summary>Báo ai-service có một câu hỏi tri thức chưa trả lời được. Chỉ MÃ BĂM của câu hỏi được lưu ở đó (không nội dung, không mã người dùng).</summary>
    Task RecordUnansweredAsync(string question, CancellationToken ct = default) => Task.CompletedTask;

    // Bộ nhớ dài hạn: các lời gọi này mang MÃ BĂM SHA-256 của người dùng (do client tính), không bao giờ mang id thật.
    Task<IReadOnlyList<AgentMemoryEntry>> GetMemoryAsync(Guid userId, CancellationToken ct = default);

    Task PutMemoryAsync(Guid userId, string key, string value, CancellationToken ct = default);

    /// <param name="key">Null = xóa hết. Trả số dòng đã xóa.</param>
    Task<int> DeleteMemoryAsync(Guid userId, string? key, CancellationToken ct = default);
}
