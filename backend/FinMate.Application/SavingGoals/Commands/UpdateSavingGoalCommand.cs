namespace FinMate.Application.SavingGoals.Commands;

public record UpdateSavingGoalCommand(
    Guid UserId,
    Guid GoalId,
    string Name,
    long TargetCents,
    DateTimeOffset? Deadline);
