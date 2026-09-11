namespace FinMate.Domain.Entities.Gamification;

/// <summary>
/// Trạng thái gamification của một user (1-1). Tạo lazily lần đầu user phát sinh hoạt động,
/// không backfill cho toàn bộ user cũ.
/// </summary>
public class UserGamification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public int Level { get; set; } = 1;

    /// <summary>EXP tích lũy trọn đời — level luôn suy ra từ con số này.</summary>
    public int ExpPoints { get; set; }

    public int CurrentStreakDays { get; set; }
    public int LongestStreakDays { get; set; }

    /// <summary>Ngày hoạt động gần nhất theo lịch VN.</summary>
    public DateOnly? LastActivityDate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User? User { get; set; }
}
