namespace FinMate.Domain.Entities.Gamification;

/// <summary>
/// Tiến độ của một user với một mission trong một chu kỳ. Dòng của chu kỳ hiện tại được tạo
/// lazily khi user phát sinh hoạt động hoặc mở màn hình mission — cùng cách budget_periods
/// làm ở Phase 5, để không sinh dòng cho user không hoạt động.
/// </summary>
public class UserMission
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid MissionId { get; set; }

    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }

    public int Progress { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int ExpAwarded { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Mission? Mission { get; set; }
}
