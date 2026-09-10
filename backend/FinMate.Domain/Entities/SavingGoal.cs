namespace FinMate.Domain.Entities;

// Bảng tối thiểu — chỉ để Transaction.SavingGoalId có FK trỏ tới. Full CRUD
// (Command/Query/Controller/Repository) thuộc Phase 5, xem .context/TASKS.md.
public class SavingGoal
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long TargetCents { get; set; }
    public long SavedCents { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset? Deadline { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public User? User { get; set; }
}
