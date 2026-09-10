namespace FinMate.Application.Common.Models;

public record SavingGoalDto(
    Guid Id,
    string Name,
    long TargetCents,
    long SavedCents,
    long RemainingCents,
    int PercentComplete,
    string Status,
    DateTimeOffset? Deadline,
    DateTimeOffset? CompletedAt);

public record GoalContributionDto(
    Guid Id,
    long AmountCents,
    string? Note,
    DateTimeOffset ContributedAt);

public record GoalProgressDto(
    Guid GoalId,
    string Name,
    string Status,
    long TargetCents,
    long SavedCents,
    long RemainingCents,
    int PercentComplete,
    DateTimeOffset? Deadline,
    int? DaysRemaining,
    long? RequiredPerDayCents,
    bool IsOnTrack,
    IReadOnlyList<GoalContributionDto> RecentContributions);
