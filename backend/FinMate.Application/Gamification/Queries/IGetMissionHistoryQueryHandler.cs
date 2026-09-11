using FinMate.Application.Common.Models;

namespace FinMate.Application.Gamification.Queries;

public interface IGetMissionHistoryQueryHandler
{
    Task<IReadOnlyList<MissionDto>> HandleAsync(GetMissionHistoryQuery query, CancellationToken ct = default);
}
