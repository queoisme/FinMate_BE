using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Queries;

public interface IGetMissionListQueryHandler
{
    Task<IReadOnlyList<AdminMissionDto>> HandleAsync(
        GetMissionListQuery query, CancellationToken ct = default);
}
