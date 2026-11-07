using FinMate.Application.Agent;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>
/// Giả định kinh tế cho agent dự báo (giá xe, giá nhà, lãi suất...). Mọi giả định có nguồn + ngày của số liệu; đổi giá trị = thêm phiên bản mới (không sửa tại chỗ),
/// chỉ phiên bản đã duyệt mới nhất được agent dùng. Mọi thao tác ghi để lại một dòng audit_logs.
/// </summary>
[Route("api/v1/admin/assumptions")]
public class AdminAssumptionsController(IAssumptionService service) : AdminControllerBase
{
    public sealed record CreateAssumptionRequest(string Key, int RateBps, string Unit, string SourceName, string SourceUrl, DateTimeOffset AsOf);

    /// <summary>Mọi phiên bản (khóa rồi mới nhất trước); <c>?key=</c> để lọc theo một khóa.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? key, CancellationToken ct)
        => Ok(ApiResponse<List<AssumptionDto>>.Ok((await service.ListAsync(key, ct)).Select(AssumptionDto.From).ToList()));

    /// <summary>Tạo bản nháp. Lỗi 422: <c>invalid_assumption_key</c>, <c>invalid_assumption_rate</c>, <c>invalid_assumption_unit</c>, <c>assumption_source_required</c>.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAssumptionRequest body, CancellationToken ct)
    {
        var created = await service.CreateDraftAsync(CurrentAdminId, body.Key, body.RateBps, body.Unit, body.SourceName, body.SourceUrl, body.AsOf, CurrentIpAddress, ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<AssumptionDto>.Ok(AssumptionDto.From(created)));
    }

    /// <summary>Duyệt bản nháp: bản đã duyệt trước của cùng khóa tự về Retired. 404 nếu không có, 409 <c>assumption_not_draft</c> nếu không phải bản nháp.</summary>
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
        => Ok(ApiResponse<AssumptionDto>.Ok(AssumptionDto.From(await service.ApproveAsync(id, CurrentAdminId, CurrentIpAddress, ct))));

    /// <summary>Gỡ một phiên bản; agent thôi dùng nó ngay. 409 <c>assumption_already_retired</c> nếu đã gỡ.</summary>
    [HttpPost("{id:guid}/retire")]
    public async Task<IActionResult> Retire(Guid id, CancellationToken ct)
        => Ok(ApiResponse<AssumptionDto>.Ok(AssumptionDto.From(await service.RetireAsync(id, CurrentAdminId, CurrentIpAddress, ct))));
}
