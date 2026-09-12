using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Queries;

public class GetAuditLogListQueryHandler : IGetAuditLogListQueryHandler
{
    private readonly IAuditLogRepository _auditLogRepository;

    public GetAuditLogListQueryHandler(IAuditLogRepository auditLogRepository)
    {
        _auditLogRepository = auditLogRepository;
    }

    public async Task<AuditLogListDto> HandleAsync(
        GetAuditLogListQuery query, CancellationToken ct = default)
    {
        var result = await _auditLogRepository.GetPageAsync(
            new AuditLogListFilter(
                query.UserId,
                query.EventType,
                query.FromDate,
                query.ToDate,
                query.Cursor,
                AdminPaging.Clamp(query.Limit)),
            ct);

        var items = result.Items
            .Select(a => new AuditLogDto(a.Id, a.UserId, a.EventType, a.Metadata, a.IpAddress, a.CreatedAt))
            .ToList();

        return new AuditLogListDto(items, result.NextCursor);
    }
}
