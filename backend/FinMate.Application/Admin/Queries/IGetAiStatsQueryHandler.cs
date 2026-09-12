using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Queries;

public interface IGetAiStatsQueryHandler
{
    Task<AiStatsDto> HandleAsync(GetAiStatsQuery query, CancellationToken ct = default);
}
