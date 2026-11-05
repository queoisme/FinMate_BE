using FinMate.API.Authorization;
using FinMate.Application.Common.Models;
using FinMate.Application.Monitoring;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Ops;

[Route("api/v1/ops/monitoring")]
[Authorize(Policy = AuthorizationPolicies.ManagerOrAbove)]
public class OpsMonitoringController : OpsControllerBase
{
    private readonly IOpsMonitoringService _monitoring;

    public OpsMonitoringController(IOpsMonitoringService monitoring)
    {
        _monitoring = monitoring;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
        => Ok(ApiResponse<MonitoringStatusDto>.Ok(await _monitoring.GetStatusAsync(CurrentActorId, ct)));

    [HttpGet("ocr")]
    public async Task<IActionResult> Ocr(CancellationToken ct)
        => Ok(ApiResponse<OcrStatsDto>.Ok(await _monitoring.GetOcrStatsAsync(CurrentActorId, ct)));

    /// <summary>Không truyền <c>status</c> thì chỉ trả sự cố chưa đóng.</summary>
    [HttpGet("incidents")]
    public async Task<IActionResult> Incidents(
        [FromQuery] IncidentStatus? status,
        [FromQuery] IncidentSeverity? severity,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
        => Ok(ApiResponse<OpsIncidentPageDto>.Ok(await _monitoring.ListAsync(CurrentActorId, status, severity, offset, limit, ct)));

    [HttpGet("incidents/{id:guid}")]
    public async Task<IActionResult> Incident(Guid id, CancellationToken ct)
        => Ok(ApiResponse<OpsIncidentDto>.Ok(await _monitoring.GetAsync(CurrentActorId, id, ct)));

    [HttpPost("incidents/{id:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken ct)
        => Ok(ApiResponse<OpsIncidentDto>.Ok(await _monitoring.AcknowledgeAsync(CurrentActorId, id, CurrentIpAddress, ct)));

    [HttpPost("incidents/{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] ResolveIncidentRequest request, CancellationToken ct)
        => Ok(ApiResponse<OpsIncidentDto>.Ok(await _monitoring.ResolveAsync(CurrentActorId, id, request.Note, CurrentIpAddress, ct)));
}

public record ResolveIncidentRequest(string? Note);
