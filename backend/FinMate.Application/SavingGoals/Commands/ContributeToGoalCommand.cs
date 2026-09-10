namespace FinMate.Application.SavingGoals.Commands;

public record ContributeToGoalCommand(
    Guid UserId,
    Guid GoalId,
    long AmountCents,
    string? Note,
    DateTimeOffset? ContributedAt);
