using FinMate.Application.Common.Models;

namespace FinMate.Application.Gamification.Queries;

public interface IGetGamificationProfileQueryHandler
{
    Task<GamificationProfileDto> HandleAsync(GetGamificationProfileQuery query, CancellationToken ct = default);
}
