using FinMate.Domain.Entities.Gamification;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

/// <summary>
/// Một hoạt động đáng thưởng của user. Gộp cả EXP lẫn điều kiện mission vào một record để
/// caller không thể gọi thiếu bước nào.
/// </summary>
public record GamificationActivity(
    Guid UserId,
    MissionConditionType ConditionType,
    int ExpReward,
    DateTimeOffset OccurredAt);

public record GamificationOutcome(
    int Level,
    bool LeveledUp,
    int ExpPoints,
    int CurrentStreakDays,
    IReadOnlyList<MascotItem> UnlockedItems);

public interface IGamificationService
{
    /// <summary>
    /// Ghi nhận một hoạt động: cập nhật streak → cộng EXP → tiến mission → mở khóa mascot.
    /// Giống <c>IBudgetPeriodService</c>, service này chỉ mutate/track chứ KHÔNG gọi
    /// SaveChangesAsync — handler vẫn kết thúc bằng đúng 1 lần lưu nên gamification,
    /// transaction, balance và budget cùng nằm trong 1 DB transaction ngầm của EF Core.
    /// </summary>
    Task<GamificationOutcome> RecordActivityAsync(GamificationActivity activity, CancellationToken ct = default);

    /// <summary>Trừ lại EXP khi một hoạt động bị hủy (ví dụ xóa giao dịch đã confirmed).</summary>
    Task RevertExpAsync(Guid userId, int exp, CancellationToken ct = default);

    Task<UserGamification> GetOrCreateProfileAsync(Guid userId, CancellationToken ct = default);
}
