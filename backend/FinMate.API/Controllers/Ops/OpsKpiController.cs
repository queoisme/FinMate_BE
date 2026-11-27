using FinMate.Application.Common.Models;
using FinMate.Application.Kpi;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Ops;

/// <summary>Dashboard KPI sản phẩm (Phase 94). Quyền <c>ViewProductKpi</c>, kiểm ở service; 2FA nội bộ do policy của <see cref="OpsControllerBase"/>.</summary>
[Route("api/v1/ops/kpi")]
public class OpsKpiController : OpsControllerBase
{
    private readonly IKpiSummaryService _kpi;

    public OpsKpiController(IKpiSummaryService kpi)
    {
        _kpi = kpi;
    }

    /// <summary><c>from</c>, <c>to</c>: ngày VN <c>yyyy-MM-dd</c>, tính cả hai đầu, tối đa 366 ngày. <c>granularity</c>: <c>day</c> (mặc định), <c>week</c>, <c>month</c>.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? granularity, CancellationToken ct)
        => Ok(ApiResponse<KpiSummaryDto>.Ok(await _kpi.GetSummaryAsync(CurrentActorId, from, to, granularity, ct)));
}
