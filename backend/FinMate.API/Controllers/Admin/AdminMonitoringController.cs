using FinMate.Application.Common.Models;
using FinMate.Application.ListenerHealth;
using FinMate.Application.Monitoring;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>Danh sách lỗi, không có nội dung. Cần chi tiết thì dùng <c>traceId</c> để tra log của container.</summary>
[Route("api/v1/admin/monitoring")]
public class AdminMonitoringController : AdminControllerBase
{
    private readonly IAdminErrorEventService _errors;
    private readonly IProviderFormatStatsService _formats;

    public AdminMonitoringController(IAdminErrorEventService errors, IProviderFormatStatsService formats)
    {
        _errors = errors;
        _formats = formats;
    }

    /// <summary>BR-LH-11: từng provider đọc được bao nhiêu phần trăm thông báo trong 24 giờ và 7 ngày trước đó; kèm số ca <c>upload_gap</c> theo phiên bản app (S2).</summary>
    [HttpGet("provider-formats")]
    public async Task<IActionResult> ProviderFormats(CancellationToken ct)
        => Ok(ApiResponse<ProviderFormatsDto>.Ok(await _formats.GetAsync(ct)));

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
