using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Queries;

public interface IGetDataDeletionRequestListQueryHandler
{
    Task<DataDeletionRequestListDto> HandleAsync(
        GetDataDeletionRequestListQuery query, CancellationToken ct = default);
}
