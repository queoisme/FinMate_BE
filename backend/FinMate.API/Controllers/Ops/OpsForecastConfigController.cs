using System.Text.Json;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Models;
using FinMate.Application.ForecastConfig;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FinMate.API.Controllers.Ops;

public sealed record CreateForecastConfigDraftRequest(JsonElement? Changes, Guid? FromDraftId, int? FromVersion, string? Reason);

public sealed record UpdateForecastConfigDraftRequest(JsonElement Changes, string? Reason, DateTimeOffset? ExpectedUpdatedAt);

public sealed record ForecastConfigDecisionRequest(string? Note);

/// <summary>
/// Phase 95 (A): tham số dự báo (<c>forecast</c> + ngưỡng T1/T2 trong <c>limits</c>). Staff soạn bản nháp và gửi duyệt; OpsManager hoặc Admin duyệt / từ chối,
/// người duyệt khác người soạn. Bản nháp không ảnh hưởng runtime cho tới khi được duyệt. Quyền <c>DraftForecastConfig</c> / <c>ApproveForecastConfig</c>
/// kiểm ở service (đọc DB); 2FA do policy của <see cref="OpsControllerBase"/>. Admin vẫn sửa trực tiếp ở <c>/admin/system-config</c>.
/// </summary>
[Route("api/v1/ops/forecast-config")]
public class OpsForecastConfigController : OpsControllerBase
{
    private readonly IForecastConfigDraftService _drafts;

    public OpsForecastConfigController(IForecastConfigDraftService drafts)
    {
        _drafts = drafts;
    }

    [HttpGet]
    public async Task<IActionResult> Current(CancellationToken ct)
        => Ok(ApiResponse<ForecastConfigCurrentDto>.Ok(await _drafts.GetCurrentAsync(CurrentActorId, ct)));

    /// <summary>Mỗi tham số: kiểu, mặc định, khoảng hợp lệ, ràng buộc với trường khác, mô tả.</summary>
    [HttpGet("meta")]
    public async Task<IActionResult> Meta(CancellationToken ct)
    {
        await _drafts.GetCurrentAsync(CurrentActorId, ct);   // cùng quyền đọc
        return Ok(ApiResponse<IReadOnlyList<ForecastConfigField>>.Ok(ForecastConfigMeta.Fields()));
    }

    /// <summary><c>status</c>: <c>draft</c>, <c>pending_approval</c>, <c>rejected</c>, <c>cancelled</c>, <c>active</c>, <c>archived</c>.</summary>
    [HttpGet("drafts")]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int offset, [FromQuery] int limit, CancellationToken ct)
    {
        var page = await _drafts.ListAsync(CurrentActorId, ParseStatus(status), offset, limit, ct);
        return Ok(ApiResponse<IReadOnlyList<ForecastConfigDraftDto>>.Ok(page.Items, page.ToMeta()));
    }

    [HttpPost("drafts")]
    public async Task<IActionResult> Create([FromBody] CreateForecastConfigDraftRequest request, CancellationToken ct)
    {
        var draft = await _drafts.CreateAsync(
            CurrentActorId, new CreateForecastConfigDraft(request.Changes, request.FromDraftId, request.FromVersion, request.Reason), CurrentIpAddress, ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<ForecastConfigDraftDto>.Ok(draft));
    }

    [HttpGet("drafts/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(ApiResponse<ForecastConfigDraftDto>.Ok(await _drafts.GetAsync(CurrentActorId, id, ct)));

    [HttpPatch("drafts/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateForecastConfigDraftRequest request, CancellationToken ct)
        => Ok(ApiResponse<ForecastConfigDraftDto>.Ok(await _drafts.UpdateAsync(
            CurrentActorId, id, new UpdateForecastConfigDraft(request.Changes, request.Reason, request.ExpectedUpdatedAt), CurrentIpAddress, ct)));

    [HttpPost("drafts/{id:guid}/submit")]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
        => Ok(ApiResponse<ForecastConfigDraftDto>.Ok(await _drafts.SubmitAsync(CurrentActorId, id, CurrentIpAddress, ct)));

    [HttpPost("drafts/{id:guid}/withdraw")]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct)
        => Ok(ApiResponse<ForecastConfigDraftDto>.Ok(await _drafts.WithdrawAsync(CurrentActorId, id, CurrentIpAddress, ct)));

    [HttpPost("drafts/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        => Ok(ApiResponse<ForecastConfigDraftDto>.Ok(await _drafts.CancelAsync(CurrentActorId, id, CurrentIpAddress, ct)));

    [HttpPost("drafts/{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ForecastConfigDecisionRequest? request, CancellationToken ct)
        => Ok(ApiResponse<ForecastConfigDraftDto>.Ok(await _drafts.ApproveAsync(CurrentActorId, id, request?.Note, CurrentIpAddress, ct)));

    [HttpPost("drafts/{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ForecastConfigDecisionRequest request, CancellationToken ct)
        => Ok(ApiResponse<ForecastConfigDraftDto>.Ok(await _drafts.RejectAsync(CurrentActorId, id, request.Note, CurrentIpAddress, ct)));

    private static SystemConfigStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        return Enum.TryParse<SystemConfigStatus>(status.Replace("_", string.Empty), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(status, out _)
            ? parsed
            : throw new BadRequestException("FORECAST_CONFIG_INVALID_STATUS", "Trạng thái không hợp lệ.");
    }
}
