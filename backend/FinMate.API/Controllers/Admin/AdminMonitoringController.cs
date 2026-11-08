using FinMate.Application.Common.Models;
using FinMate.Application.Monitoring;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>Danh sách lỗi, không có nội dung. Cần chi tiết thì dùng <c>traceId</c> để tra log của container.</summary>
[Route("api/v1/admin/monitoring")]
public class AdminMonitoringController : AdminControllerBase
{
    private readonly IAdminErrorEventService _errors;

    public AdminMonitoringController(IAdminErrorEventService errors)
    {
        _errors = errors;
    }

    [HttpGet("error-events")]
    public async Task<IActionResult> ErrorEvents(
        [FromQuery] string? service,
        [FromQuery] string? exceptionType,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
        => Ok(ApiResponse<OpsErrorEventPageDto>.Ok(await _errors.ListAsync(new ErrorEventQuery(service, exceptionType, from, to, offset, limit), ct)));
}
