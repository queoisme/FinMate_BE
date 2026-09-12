using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Queries;

public interface IGetAuditLogListQueryHandler
{
    Task<AuditLogListDto> HandleAsync(GetAuditLogListQuery query, CancellationToken ct = default);
}
