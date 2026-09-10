namespace FinMate.Application.SavingGoals.Commands;

public record CreateSavingGoalCommand(Guid UserId, string Name, long TargetCents, DateTimeOffset? Deadline);
