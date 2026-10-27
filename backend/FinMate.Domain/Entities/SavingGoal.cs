using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

public class SavingGoal
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long TargetCents { get; set; }
    public long SavedCents { get; set; }
    public SavingGoalStatus Status { get; set; } = SavingGoalStatus.Active;
    public DateTimeOffset? Deadline { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Đánh dấu đã nhắc quá hạn — để GoalDeadlineCheckJob không gửi lại mỗi ngày.</summary>
    public DateTimeOffset? DeadlineNotifiedAt { get; set; }

    /// <summary>Mẫu mục tiêu đã dùng để tạo. NULL = mục tiêu người dùng tự đặt.</summary>
    public Guid? GoalTemplateId { get; set; }

    /// <summary>Phương pháp đang dùng lúc tạo mục tiêu (để biết mục tiêu này theo <c>goal_mode</c> nào). NULL = tạo khi chưa dùng phương pháp nào.</summary>
    public Guid? UserMethodId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public User? User { get; set; }
    public ICollection<GoalContribution> Contributions { get; set; } = new List<GoalContribution>();
}
