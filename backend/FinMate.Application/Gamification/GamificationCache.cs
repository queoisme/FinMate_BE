using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Gamification;

/// <summary>
/// Xóa cache gamification sau khi EXP/streak/mission đổi (ARCHITECTURE.md §6). Gom một chỗ
/// để 4 handler không quên mất một trong hai key.
/// </summary>
internal static class GamificationCache
{
    internal static async Task InvalidateAsync(ICacheService cache, Guid userId, CancellationToken ct)
    {
        await cache.RemoveAsync(CacheKeys.Gamification(userId), ct);
        await cache.RemoveAsync(CacheKeys.ActiveMissions(userId), ct);
    }
}
