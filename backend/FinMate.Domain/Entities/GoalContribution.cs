namespace FinMate.Domain.Entities;

public class GoalContribution
{
    public Guid Id { get; set; }
    public Guid SavingGoalId { get; set; }
    public Guid UserId { get; set; }

    public long AmountCents { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset ContributedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public SavingGoal? SavingGoal { get; set; }
}
