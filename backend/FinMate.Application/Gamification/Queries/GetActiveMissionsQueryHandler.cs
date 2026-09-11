using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Gamification.Queries;

public class GetActiveMissionsQueryHandler : IGetActiveMissionsQueryHandler
{
    private readonly IMissionRepository _missionRepository;
    private readonly ICacheService _cache;

    public GetActiveMissionsQueryHandler(IMissionRepository missionRepository, ICacheService cache)
    {
        _missionRepository = missionRepository;
        _cache = cache;
    }

    public async Task<IReadOnlyList<MissionDto>> HandleAsync(
        GetActiveMissionsQuery query,
        CancellationToken ct = default)
    {
        var cacheKey = CacheKeys.ActiveMissions(query.UserId);
        var cached = await _cache.GetAsync<List<MissionDto>>(cacheKey, ct);
        if (cached is not null)
        {
            return cached;
        }

        var today = MissionCalendar.Today();
        var missions = await _missionRepository.GetActiveMissionsAsync(ct);
        var userMissions = await _missionRepository.GetActiveUserMissionsAsync(query.UserId, today, ct);

        var result = new List<MissionDto>(missions.Count);

        foreach (var mission in missions)
        {
            var (periodStart, periodEnd) = MissionCalendar.PeriodFor(mission.PeriodType, today);
            var userMission = userMissions.FirstOrDefault(
                m => m.MissionId == mission.Id && m.PeriodStart == periodStart);

            // Mission chưa được chạm tới chưa có dòng trong DB (tạo lazily) — vẫn phải hiện
            // ra với tiến độ 0, nếu không user sẽ không biết mình có nhiệm vụ nào.
            result.Add(userMission is null
                ? GamificationMapper.ToUntouchedDto(mission, periodStart, periodEnd)
                : GamificationMapper.ToDto(userMission, mission));
        }

        await _cache.SetAsync(cacheKey, result, CacheKeys.ActiveMissionsTtl, ct);

        return result;
    }
}
