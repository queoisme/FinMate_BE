using FinMate.Application.Common.Models;

namespace FinMate.Application.Gamification.Queries;

public interface IGetActiveMissionsQueryHandler
{
    Task<IReadOnlyList<MissionDto>> HandleAsync(GetActiveMissionsQuery query, CancellationToken ct = default);
}
