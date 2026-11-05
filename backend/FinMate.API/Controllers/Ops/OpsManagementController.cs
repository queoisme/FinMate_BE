using FinMate.API.Authorization;
using FinMate.Application.Common.Models;
using FinMate.Application.Ops;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Ops;

[Route("api/v1/ops/approvals")]
public class OpsApprovalsController : OpsControllerBase
{
    private readonly IOpsApprovalService _approvals;

    public OpsApprovalsController(IOpsApprovalService approvals)
    {
        _approvals = approvals;
    }

    [HttpPost]
    public async Task<IActionResult> Propose([FromBody] OpsProposeApprovalRequest request, CancellationToken ct)
        => Ok(ApiResponse<OpsApprovalDto>.Ok(await _approvals.ProposeAsync(
            CurrentActorId,
            new ProposeApprovalCommand(request.Type, request.TargetId, request.NewOwnerUserId, request.TicketId, request.Reason),
            CurrentIpAddress,
            ct)));

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] OpsApprovalStatus? status, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<OpsApprovalDto>>.Ok(await _approvals.ListAsync(CurrentActorId, status, ct)));

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = AuthorizationPolicies.ManagerOrAbove)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] OpsApprovalDecisionRequest request, CancellationToken ct)
        => Ok(ApiResponse<OpsApprovalDto>.Ok(await _approvals.ApproveAsync(CurrentActorId, id, request.Note, CurrentIpAddress, ct)));

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = AuthorizationPolicies.ManagerOrAbove)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] OpsApprovalDecisionRequest request, CancellationToken ct)
        => Ok(ApiResponse<OpsApprovalDto>.Ok(await _approvals.RejectAsync(CurrentActorId, id, request.Note, CurrentIpAddress, ct)));
}

[Route("api/v1/ops")]
public class OpsDirectoryController : OpsControllerBase
{
    private readonly IOpsUserService _users;

    public OpsDirectoryController(IOpsUserService users)
    {
        _users = users;
    }

    [HttpGet("users/{id:guid}")]
    public async Task<IActionResult> GetUser(Guid id, CancellationToken ct)
        => Ok(ApiResponse<OpsUserDto>.Ok(await _users.GetUserAsync(CurrentActorId, id, ct)));

    [HttpPost("users/{id:guid}/emergency-lock")]
    public async Task<IActionResult> EmergencyLock(Guid id, [FromBody] OpsEmergencyLockRequest request, CancellationToken ct)
        => Ok(ApiResponse<OpsUserDto>.Ok(await _users.EmergencyLockAsync(CurrentActorId, id, request.TicketId, request.Reason, CurrentIpAddress, ct)));

    [HttpGet("groups/{id:guid}")]
    public async Task<IActionResult> GetGroup(Guid id, [FromQuery] Guid ticketId, CancellationToken ct)
        => Ok(ApiResponse<OpsGroupDto>.Ok(await _users.GetGroupAsync(CurrentActorId, id, ticketId, CurrentIpAddress, ct)));
}

[Route("api/v1/ops")]
public class OpsManagerController : OpsControllerBase
{
    private readonly IOpsManagerService _manager;

    public OpsManagerController(IOpsManagerService manager)
    {
        _manager = manager;
    }

    [HttpGet("dashboard")]
    [Authorize(Policy = AuthorizationPolicies.ManagerOrAbove)]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
        => Ok(ApiResponse<OpsDashboardDto>.Ok(await _manager.GetDashboardAsync(CurrentActorId, ct)));

    /// <summary>Mọi vai trò nội bộ đọc được để dùng khi trả lời khách.</summary>
    [HttpGet("canned-replies")]
    public async Task<IActionResult> ListCannedReplies([FromQuery] bool includeInactive = false, CancellationToken ct = default)
        => Ok(ApiResponse<IReadOnlyList<CannedReplyDto>>.Ok(await _manager.ListCannedRepliesAsync(CurrentActorId, includeInactive, ct)));

    [HttpPost("canned-replies")]
    [Authorize(Policy = AuthorizationPolicies.ManagerOrAbove)]
    public async Task<IActionResult> CreateCannedReply([FromBody] OpsCannedReplyRequest request, CancellationToken ct)
        => Ok(ApiResponse<CannedReplyDto>.Ok(await _manager.CreateCannedReplyAsync(CurrentActorId, request.Title, request.Body, CurrentIpAddress, ct)));

    [HttpPatch("canned-replies/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ManagerOrAbove)]
    public async Task<IActionResult> UpdateCannedReply(Guid id, [FromBody] OpsCannedReplyPatchRequest request, CancellationToken ct)
        => Ok(ApiResponse<CannedReplyDto>.Ok(await _manager.UpdateCannedReplyAsync(CurrentActorId, id, request.Title, request.Body, request.IsActive, CurrentIpAddress, ct)));

    [HttpGet("audit-logs")]
    [Authorize(Policy = AuthorizationPolicies.ManagerOrAbove)]
    public async Task<IActionResult> AuditLogs(
        [FromQuery] string? eventType,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var page = await _manager.GetAuditLogsAsync(CurrentActorId, eventType, from, to, cursor, limit, ct);
        return Ok(ApiResponse<AuditLogListDto>.Ok(page));
    }

    [HttpGet("ai-stats")]
    [Authorize(Policy = AuthorizationPolicies.ManagerOrAbove)]
    public async Task<IActionResult> AiStats([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
        => Ok(ApiResponse<AiStatsDto>.Ok(await _manager.GetAiStatsAsync(CurrentActorId, from, to, ct)));
}

public record OpsProposeApprovalRequest(OpsApprovalType Type, Guid TargetId, Guid? NewOwnerUserId, Guid? TicketId, string? Reason);
public record OpsApprovalDecisionRequest(string? Note);
public record OpsEmergencyLockRequest(Guid TicketId, string? Reason);
public record OpsCannedReplyRequest(string? Title, string? Body);
public record OpsCannedReplyPatchRequest(string? Title, string? Body, bool? IsActive);
