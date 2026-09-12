using FinMate.Application.Admin.Queries;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

[Route("api/v1/admin/audit-logs")]
public class AdminAuditLogsController : AdminControllerBase
{
    private readonly IGetAuditLogListQueryHandler _listHandler;

    public AdminAuditLogsController(IGetAuditLogListQueryHandler listHandler)
    {
        _listHandler = listHandler;
    }

    /// <summary>
    /// Chỉ đọc. Không có endpoint sửa hay xoá audit log — nhật ký kiểm toán mà admin sửa được
    /// thì không còn là nhật ký kiểm toán.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] Guid? userId,
        [FromQuery] string? eventType,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? cursor,
        [FromQuery] int limit,
        CancellationToken ct)
    {
        var result = await _listHandler.HandleAsync(
            new GetAuditLogListQuery(userId, eventType, from, to, cursor, limit), ct);

        return Ok(ApiResponse<IReadOnlyList<AuditLogDto>>.Ok(
            result.Items, new ApiMeta(result.NextCursor)));
    }
}
