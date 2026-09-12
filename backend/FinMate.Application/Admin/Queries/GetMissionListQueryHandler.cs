using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Queries;

public class GetMissionListQueryHandler : IGetMissionListQueryHandler
{
    private readonly IMissionRepository _missionRepository;

    public GetMissionListQueryHandler(IMissionRepository missionRepository)
    {
        _missionRepository = missionRepository;
    }

    public async Task<IReadOnlyList<AdminMissionDto>> HandleAsync(
        GetMissionListQuery query, CancellationToken ct = default)
    {
        var missions = await _missionRepository.GetAllMissionsAsync(query.IncludeInactive, ct);
        return missions.Select(AdminMapper.ToDto).ToList();
    }
}
