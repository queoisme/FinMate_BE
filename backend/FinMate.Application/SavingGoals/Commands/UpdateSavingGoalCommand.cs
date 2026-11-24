using FinMate.Domain.Enums;

namespace FinMate.Application.SavingGoals.Commands;

public record UpdateSavingGoalCommand(
    Guid UserId,
    Guid GoalId,
    string Name,
    long TargetCents,
    DateTimeOffset? Deadline,
    string? Icon = null,
    GoalContributionFrequency? ContributionFrequency = null,
    string? MarketTopic = null);
