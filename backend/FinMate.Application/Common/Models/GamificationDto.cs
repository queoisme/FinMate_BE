namespace FinMate.Application.Common.Models;

public record GamificationProfileDto(
    int Level,
    int ExpPoints,
    int ExpForCurrentLevel,
    int ExpForNextLevel,
    int ExpToNextLevel,
    int CurrentStreakDays,
    int LongestStreakDays,
    DateOnly? LastActivityDate);

public record MissionDto(
    Guid MissionId,
    string Code,
    string Title,
    string Description,
    string PeriodType,
    int Progress,
    int Target,
    int ExpReward,
    bool IsCompleted,
    DateTimeOffset? CompletedAt,
    DateOnly PeriodStart,
    DateOnly? PeriodEnd);

public record MascotItemDto(
    Guid Id,
    string Code,
    string Name,
    string ItemType,
    bool IsOwned,
    bool IsEquipped,
    string UnlockType,
    /// <summary>Điều kiện mở khóa dạng chữ cho item chưa sở hữu; null nếu đã sở hữu.</summary>
    string? UnlockHint);

public record MascotInventoryDto(IReadOnlyList<MascotItemDto> Items);
