using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities.Gamification;
using FinMate.Domain.Enums;

namespace FinMate.Application.Gamification;

public class GamificationService : IGamificationService
{
    private readonly IGamificationRepository _gamificationRepository;
    private readonly IMissionRepository _missionRepository;

    public GamificationService(
        IGamificationRepository gamificationRepository,
        IMissionRepository missionRepository)
    {
        _gamificationRepository = gamificationRepository;
        _missionRepository = missionRepository;
    }

    public async Task<UserGamification> GetOrCreateProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await _gamificationRepository.GetProfileAsync(userId, ct);
        if (profile is not null)
        {
            return profile;
        }

        var now = DateTimeOffset.UtcNow;
        profile = new UserGamification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Level = 1,
            ExpPoints = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _gamificationRepository.AddProfile(profile);

        return profile;
    }

    public async Task<GamificationOutcome> RecordActivityAsync(
        GamificationActivity activity,
        CancellationToken ct = default)
    {
        var profile = await GetOrCreateProfileAsync(activity.UserId, ct);
        var levelBefore = profile.Level;

        UpdateStreak(profile, activity.OccurredAt);

        // EXP thưởng của mission cộng vào cùng lượt, nên một hoạt động vừa hoàn thành mission
        // có thể nhảy nhiều level một lúc — level phải tính lại sau khi cộng hết.
        var (missionReward, completedCodes) = await AdvanceMissionsAsync(activity, profile, ct);

        ApplyExp(profile, activity.ExpReward + missionReward);

        var unlocked = await UnlockEligibleItemsAsync(activity.UserId, profile.Level, completedCodes, ct);

        return new GamificationOutcome(
            profile.Level,
            profile.Level > levelBefore,
            profile.ExpPoints,
            profile.CurrentStreakDays,
            unlocked);
    }

    public async Task RevertExpAsync(Guid userId, int exp, CancellationToken ct = default)
    {
        if (exp <= 0)
        {
            return;
        }

        var profile = await _gamificationRepository.GetProfileAsync(userId, ct);
        if (profile is null)
        {
            return;
        }

        ApplyExp(profile, -exp);
    }

    private static void ApplyExp(UserGamification profile, int delta)
    {
        profile.ExpPoints = Math.Max(0, profile.ExpPoints + delta);
        profile.Level = LevelCurve.LevelForExp(profile.ExpPoints);
        profile.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void UpdateStreak(UserGamification profile, DateTimeOffset occurredAt)
    {
        var today = Common.VietnamTime.DateOf(occurredAt);

        if (profile.LastActivityDate == today)
        {
            return;
        }

        profile.CurrentStreakDays = profile.LastActivityDate == today.AddDays(-1)
            ? profile.CurrentStreakDays + 1
            : 1;

        profile.LastActivityDate = today;
        profile.LongestStreakDays = Math.Max(profile.LongestStreakDays, profile.CurrentStreakDays);
        profile.UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Tiến độ mission khớp điều kiện; trả về EXP thưởng vừa kiếm được và mã của các mission
    /// vừa hoàn thành trong chính lượt này.
    /// </summary>
    private async Task<(int Reward, HashSet<string> CompletedCodes)> AdvanceMissionsAsync(
        GamificationActivity activity,
        UserGamification profile,
        CancellationToken ct)
    {
        var completedCodes = new HashSet<string>(StringComparer.Ordinal);
        var missions = await _missionRepository.GetActiveMissionsByConditionAsync(activity.ConditionType, ct);
        if (missions.Count == 0)
        {
            return (0, completedCodes);
        }

        var today = Common.VietnamTime.DateOf(activity.OccurredAt);
        var reward = 0;

        // Nhóm theo chu kỳ để mỗi chu kỳ chỉ truy vấn UserMission một lần.
        foreach (var group in missions.GroupBy(m => MissionCalendar.PeriodFor(m.PeriodType, today)))
        {
            var (periodStart, periodEnd) = group.Key;
            var missionIds = group.Select(m => m.Id).ToArray();
            var existing = await _missionRepository.GetUserMissionsAsync(activity.UserId, missionIds, periodStart, ct);

            foreach (var mission in group)
            {
                var userMission = existing.FirstOrDefault(x => x.MissionId == mission.Id);

                if (userMission is null)
                {
                    userMission = NewUserMission(activity.UserId, mission.Id, periodStart, periodEnd);
                    _missionRepository.AddUserMission(userMission);
                }

                if (userMission.IsCompleted)
                {
                    continue;
                }

                // Streak là trạng thái tuyệt đối, không phải số lần cộng dồn.
                userMission.Progress = mission.ConditionType == MissionConditionType.LoginStreak
                    ? profile.CurrentStreakDays
                    : userMission.Progress + 1;

                userMission.UpdatedAt = DateTimeOffset.UtcNow;

                if (userMission.Progress >= mission.ConditionTarget)
                {
                    userMission.IsCompleted = true;
                    userMission.CompletedAt = DateTimeOffset.UtcNow;
                    userMission.ExpAwarded = mission.ExpReward;
                    reward += mission.ExpReward;
                    completedCodes.Add(mission.Code);
                }
            }
        }

        return (reward, completedCodes);
    }

    private static UserMission NewUserMission(Guid userId, Guid missionId, DateOnly periodStart, DateOnly periodEnd)
    {
        var now = DateTimeOffset.UtcNow;
        return new UserMission
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MissionId = missionId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Progress = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private async Task<IReadOnlyList<MascotItem>> UnlockEligibleItemsAsync(
        Guid userId, int level, IReadOnlySet<string> justCompletedMissionCodes, CancellationToken ct)
    {
        var all = await _gamificationRepository.GetAllMascotItemsAsync(ct);
        var owned = await _gamificationRepository.GetOwnedMascotItemsAsync(userId, ct);
        var ownedIds = owned.Select(o => o.MascotItemId).ToHashSet();

        var unlocked = new List<MascotItem>();

        foreach (var item in all.Where(i => !i.IsPremium && !ownedIds.Contains(i.Id)))
        {
            var eligible = item.UnlockType switch
            {
                MascotUnlockType.Default => true,
                MascotUnlockType.Level => level >= (item.UnlockLevel ?? int.MaxValue),
                // Mission vừa hoàn thành trong lượt này mới chỉ được track, chưa lưu, nên
                // truy vấn DB sẽ không thấy — phải xét riêng, nếu không phần thưởng mascot
                // chỉ xuất hiện ở lần hoạt động kế tiếp.
                MascotUnlockType.Mission => item.UnlockMissionCode is not null
                    && (justCompletedMissionCodes.Contains(item.UnlockMissionCode)
                        || await _missionRepository.HasEverCompletedAsync(userId, item.UnlockMissionCode, ct)),
                _ => false,
            };

            if (!eligible)
            {
                continue;
            }

            _gamificationRepository.AddOwnedMascotItem(new UserMascotItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                MascotItemId = item.Id,
                ItemType = item.ItemType,
                UnlockedAt = DateTimeOffset.UtcNow,
                IsEquipped = false,
            });

            unlocked.Add(item);
        }

        return unlocked;
    }
}
