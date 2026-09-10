namespace FinMate.Application.SavingGoals.Commands;

public record CancelSavingGoalCommand(Guid UserId, Guid GoalId);
