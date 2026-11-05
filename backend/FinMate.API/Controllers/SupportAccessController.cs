using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>Khách đồng ý, từ chối hoặc thu hồi quyền xem dữ liệu, và xem ai đã xem gì.</summary>
[ApiController]
[Route("api/v1/support")]
[Authorize]
public class SupportAccessController : ControllerBase
{
    private readonly ISupportAccessService _access;

    public SupportAccessController(ISupportAccessService access)
    {
        _access = access;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("tickets/{id:guid}/access-requests")]
    public async Task<IActionResult> List(Guid id, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<SupportAccessRequestDto>>.Ok(await _access.ListAsync(CurrentUserId, id, ct)));

    /// <summary>Yêu cầu có phạm vi <c>Draft</c> thì phải chọn ví nhận giao dịch nháp.</summary>
    [HttpPost("tickets/{id:guid}/access-requests/{requestId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, Guid requestId, [FromBody] ApproveSupportAccessRequest request, CancellationToken ct)
        => Ok(ApiResponse<SupportAccessRequestDto>.Ok(await _access.ApproveAsync(CurrentUserId, id, requestId, request.FinancialAccountId, ct)));

    [HttpPost("tickets/{id:guid}/access-requests/{requestId:guid}/deny")]
    public async Task<IActionResult> Deny(Guid id, Guid requestId, CancellationToken ct)
        => Ok(ApiResponse<SupportAccessRequestDto>.Ok(await _access.DenyAsync(CurrentUserId, id, requestId, ct)));

    [HttpPost("tickets/{id:guid}/access-requests/{requestId:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, Guid requestId, CancellationToken ct)
        => Ok(ApiResponse<SupportAccessRequestDto>.Ok(await _access.RevokeAsync(CurrentUserId, id, requestId, ct)));

    [HttpGet("access-log")]
    public async Task<IActionResult> AccessLog(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<SupportAccessLogDto>>.Ok(await _access.AccessLogAsync(CurrentUserId, ct)));
}

public record ApproveSupportAccessRequest(Guid? FinancialAccountId);
