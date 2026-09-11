using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Gamification.Queries;

public class GetMissionHistoryQueryHandler : IGetMissionHistoryQueryHandler
{
    private readonly IMissionRepository _missionRepository;

    public GetMissionHistoryQueryHandler(IMissionRepository missionRepository)
    {
        _missionRepository = missionRepository;
    }

    public async Task<IReadOnlyList<MissionDto>> HandleAsync(
        GetMissionHistoryQuery query,
        CancellationToken ct = default)
    {
        var completed = await _missionRepository.GetCompletedUserMissionsAsync(query.UserId, query.Limit, ct);

        return completed
            .Where(m => m.Mission is not null)
            .Select(m => GamificationMapper.ToDto(m, m.Mission!))
            .ToList();
    }
}
