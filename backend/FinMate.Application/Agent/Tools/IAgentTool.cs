using System.Text.Json;

namespace FinMate.Application.Agent;

/// <summary>Bối cảnh một lần chạy công cụ. <see cref="UserId"/> LUÔN lấy từ JWT của lượt hỏi, không bao giờ từ tham số do LLM đưa ra.</summary>
public sealed record ToolContext(Guid UserId, DateTimeOffset Now);

public enum ToolStatus
{
    Ok,

    /// <summary>Công cụ chạy được nhưng thiếu dữ liệu để kết luận: không bịa, bộ điều phối quyết định lập lại kế hoạch hoặc trả "chưa đủ dữ liệu".</summary>
    InsufficientData,

    /// <summary>Lời gọi bị từ chối trước khi đọc bất kỳ dữ liệu nào (công cụ lạ, tham số thiếu/sai/lạ).</summary>
    Rejected,
}

public sealed record ToolResult(ToolStatus Status, IReadOnlyList<Evidence> Evidence, string? Code = null)
{
    public static ToolResult Ok(IReadOnlyList<Evidence> evidence) => new(ToolStatus.Ok, evidence);

    public static ToolResult Insufficient(string code) => new(ToolStatus.InsufficientData, [], code);

    public static ToolResult Rejected(string code) => new(ToolStatus.Rejected, [], code);
}

/// <summary>Một tham số của công cụ. <c>Type</c> là <c>"int"</c> (số nguyên, kiểm <c>Min</c>/<c>Max</c>) hoặc <c>"string"</c> (không rỗng, ≤ 64 ký tự).</summary>
public sealed record ToolArg(string Name, string Type, bool Required, long Min = 0, long Max = 0);

/// <summary>Mô tả công cụ gửi cho bộ lập kế hoạch: chỉ tên và tham số, không có mã hay dữ liệu.</summary>
public sealed record ToolSpec(string Name, string Description, IReadOnlyList<ToolArg> Args);

public interface IAgentTool
{
    ToolSpec Spec { get; }

    /// <summary>Chỉ được gọi sau khi <see cref="ToolExecutor"/> đã kiểm tên, tham số và kiểu.</summary>
    Task<ToolResult> RunAsync(ToolContext context, JsonElement args, CancellationToken ct = default);
}
