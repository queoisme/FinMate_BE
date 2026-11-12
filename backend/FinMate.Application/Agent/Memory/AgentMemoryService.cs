using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Agent;

/// <summary>
/// Bộ nhớ dài hạn của agent do CHÍNH người dùng quản lý: xem, đặt, xóa từng khóa hoặc xóa hết (xóa hết cũng xóa ngữ cảnh hội thoại ngắn hạn). Chỉ nhận khóa trong
/// <see cref="AgentLimits.MemoryKeys"/> và giá trị 1–<see cref="AgentLimits.MaxMemoryValueLength"/> ký tự. Dữ liệu nằm ở ai-service theo mã băm của người dùng.
/// </summary>
public sealed class AgentMemoryService(IAgentAiClient ai, ConversationStore conversations)
{
    public Task<IReadOnlyList<AgentMemoryEntry>> ListAsync(Guid userId, CancellationToken ct = default) => ai.GetMemoryAsync(userId, ct);

    public Task PutAsync(Guid userId, string key, string value, CancellationToken ct = default)
    {
        RequireKey(key);
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > AgentLimits.MaxMemoryValueLength)
        {
            throw new BusinessRuleException("invalid_memory_value", $"Giá trị phải có 1–{AgentLimits.MaxMemoryValueLength} ký tự.");
        }

        return ai.PutMemoryAsync(userId, key, trimmed, ct);
    }

    /// <param name="key">Null = xóa toàn bộ bộ nhớ dài hạn và ngữ cảnh ngắn hạn của người dùng.</param>
    public async Task<int> DeleteAsync(Guid userId, string? key, CancellationToken ct = default)
    {
        if (key is not null)
        {
            RequireKey(key);
        }

        var deleted = await ai.DeleteMemoryAsync(userId, key, ct);
        if (key is null)
        {
            await conversations.ClearAsync(userId, ct);
        }

        return deleted;
    }

    private static void RequireKey(string key)
    {
        if (!AgentLimits.MemoryKeys.Contains(key))
        {
            throw new BusinessRuleException("invalid_memory_key", $"Khóa không hợp lệ. Được phép: {string.Join(", ", AgentLimits.MemoryKeys)}.");
        }
    }
}
