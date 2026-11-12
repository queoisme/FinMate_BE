using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Agent;

/// <summary>
/// Ngữ cảnh ngắn hạn của một hội thoại: chỉ các "ô" có cấu trúc (<c>price_vnd</c>, <c>horizon_months</c>) của lượt trước để hiểu câu nối tiếp như "còn 3 năm thì sao?".
/// KHÔNG lưu văn bản câu hỏi hay câu trả lời. Tự hết sau <see cref="AgentLimits.ConversationTtl"/>.
/// </summary>
public sealed record ConversationState(string? LastIntent, Dictionary<string, string> Slots, int Turn, DateTimeOffset UpdatedAt);

/// <summary>Kho ngữ cảnh trên <see cref="ICacheService"/> (Redis); mỗi người dùng một khóa riêng nên không lẫn giữa hai người.</summary>
public sealed class ConversationStore(ICacheService cache)
{
    public Task<ConversationState?> GetAsync(Guid userId, CancellationToken ct = default)
        => cache.GetAsync<ConversationState>(CacheKeys.AgentConversation(userId), ct);

    public Task SaveAsync(Guid userId, ConversationState state, CancellationToken ct = default)
        => cache.SetAsync(CacheKeys.AgentConversation(userId), state, AgentLimits.ConversationTtl, ct);

    public Task ClearAsync(Guid userId, CancellationToken ct = default)
        => cache.RemoveAsync(CacheKeys.AgentConversation(userId), ct);
}
