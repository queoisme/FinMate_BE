using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;

namespace FinMate.Application.SavingGoals;

internal static class SavingGoalMapper
{
    internal static SavingGoalDto ToDto(SavingGoal goal) => new(
        goal.Id,
        goal.Name,
        goal.TargetCents,
        goal.SavedCents,
        Math.Max(0, goal.TargetCents - goal.SavedCents),
        PercentComplete(goal),
        goal.Status.ToString().ToLowerInvariant(),
        goal.Deadline,
        goal.CompletedAt);

    internal static GoalContributionDto ToDto(GoalContribution contribution) => new(
        contribution.Id,
        contribution.AmountCents,
        contribution.Note,
        contribution.ContributedAt);

    internal static int PercentComplete(SavingGoal goal)
        => goal.TargetCents <= 0 ? 0 : (int)Math.Min(100, goal.SavedCents * 100 / goal.TargetCents);
}
