using FinMate.Application.Common.Models;
using FinMate.Domain.Entities.Gamification;
using FinMate.Domain.Enums;

namespace FinMate.Application.Gamification;

internal static class GamificationMapper
{
    internal static GamificationProfileDto ToDto(UserGamification profile)
    {
        var currentLevelFloor = LevelCurve.TotalExpForLevel(profile.Level);
        var nextLevelFloor = LevelCurve.TotalExpForLevel(profile.Level + 1);

        return new GamificationProfileDto(
            profile.Level,
            profile.ExpPoints,
            currentLevelFloor,
            nextLevelFloor,
            LevelCurve.ExpToNextLevel(profile.ExpPoints),
            profile.CurrentStreakDays,
            profile.LongestStreakDays,
            profile.LastActivityDate);
    }

    internal static MissionDto ToDto(UserMission userMission, Mission mission) => new(
        mission.Id,
        mission.Code,
        mission.Title,
        mission.Description,
        PeriodSlug(mission.PeriodType),
        userMission.Progress,
        mission.ConditionTarget,
        mission.ExpReward,
        userMission.IsCompleted,
        userMission.CompletedAt,
        userMission.PeriodStart,
        // Mission one_time không có hạn kết thúc thật — trả null thay vì DateOnly.MaxValue,
        // để client không hiển thị "hết hạn 31/12/9999".
        mission.PeriodType == MissionPeriodType.OneTime ? null : userMission.PeriodEnd);

    /// <summary>Mission chưa từng được chạm tới: tiến độ 0, chưa có dòng UserMission nào.</summary>
    internal static MissionDto ToUntouchedDto(Mission mission, DateOnly periodStart, DateOnly periodEnd) => new(
        mission.Id,
        mission.Code,
        mission.Title,
        mission.Description,
        PeriodSlug(mission.PeriodType),
        0,
        mission.ConditionTarget,
        mission.ExpReward,
        false,
        null,
        periodStart,
        mission.PeriodType == MissionPeriodType.OneTime ? null : periodEnd);

    internal static string PeriodSlug(MissionPeriodType type) => type switch
    {
        MissionPeriodType.Daily => "daily",
        MissionPeriodType.Weekly => "weekly",
        MissionPeriodType.OneTime => "one_time",
        _ => type.ToString().ToLowerInvariant(),
    };

    internal static MascotItemDto ToDto(MascotItem item, UserMascotItem? owned) => new(
        item.Id,
        item.Code,
        item.Name,
        item.ItemType.ToString().ToLowerInvariant(),
        owned is not null,
        owned?.IsEquipped ?? false,
        item.UnlockType.ToString().ToLowerInvariant(),
        owned is not null ? null : UnlockHint(item));

    private static string? UnlockHint(MascotItem item) => item.UnlockType switch
    {
        MascotUnlockType.Level => $"Đạt level {item.UnlockLevel}",
        MascotUnlockType.Mission => "Hoàn thành nhiệm vụ tương ứng",
        _ => null,
    };
}
