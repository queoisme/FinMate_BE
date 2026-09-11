namespace FinMate.Application.Common.Models;

/// <summary>
/// Phần thưởng vừa nhận được kèm theo một lần đóng góp — để mascot ăn mừng ngay mà client
/// không phải gọi thêm một vòng tới /gamification.
/// </summary>
public record GoalCelebrationDto(
    bool LeveledUp,
    int Level,
    IReadOnlyList<string> UnlockedItemNames);

public record SavingGoalDto(
    Guid Id,
    string Name,
    long TargetCents,
    long SavedCents,
    long RemainingCents,
    int PercentComplete,
    string Status,
    DateTimeOffset? Deadline,
    DateTimeOffset? CompletedAt,
    GoalCelebrationDto? Celebration = null);

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
