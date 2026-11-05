using FinMate.API.Authorization;
using FinMate.Application.Common.Models;
using FinMate.Application.Support;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Ops;

[Route("api/v1/ops/tickets")]
public class OpsTicketsController : OpsControllerBase
{
    private readonly IOpsTicketService _tickets;

    public OpsTicketsController(IOpsTicketService tickets)
    {
        _tickets = tickets;
    }

    /// <summary>Hàng đợi, hạn gần nhất trước. Không truyền <c>status</c> thì bỏ qua ca đã đóng. <c>assignee</c>: <c>me</c>, <c>none</c> hoặc một id.</summary>
    [HttpGet]
    public async Task<IActionResult> Queue(
        [FromQuery] SupportTicketStatus? status,
        [FromQuery] SupportTicketPriority? priority,
        [FromQuery] SupportTicketCategory? category,
        [FromQuery] string? assignee,
        [FromQuery] Guid? parentTicketId,
        [FromQuery] bool overdue = false,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
        => Ok(ApiResponse<OpsTicketPageDto>.Ok(await _tickets.QueueAsync(
            CurrentActorId, new OpsTicketQuery(status, priority, category, assignee, overdue, parentTicketId, offset, limit), ct)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(ApiResponse<OpsTicketDetailDto>.Ok(await _tickets.GetAsync(CurrentActorId, id, ct)));

    [HttpPost("{id:guid}/claim")]
    public async Task<IActionResult> Claim(Guid id, CancellationToken ct)
        => Ok(ApiResponse<OpsTicketDetailDto>.Ok(await _tickets.ClaimAsync(CurrentActorId, id, CurrentIpAddress, ct)));

    /// <summary><c>internal = true</c> là ghi chú nội bộ, khách không thấy.</summary>
    [HttpPost("{id:guid}/messages")]
    public async Task<IActionResult> Reply(Guid id, [FromBody] OpsTicketMessageRequest request, CancellationToken ct)
        => Ok(ApiResponse<OpsTicketDetailDto>.Ok(await _tickets.ReplyAsync(CurrentActorId, id, request.Body, request.Internal, ct)));

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] OpsTicketStatusRequest request, CancellationToken ct)
        => Ok(ApiResponse<OpsTicketDetailDto>.Ok(await _tickets.SetStatusAsync(CurrentActorId, id, request.Status, CurrentIpAddress, ct)));

    [HttpPost("{id:guid}/escalate")]
    public async Task<IActionResult> Escalate(Guid id, [FromBody] OpsTicketEscalateRequest request, CancellationToken ct)
        => Ok(ApiResponse<OpsTicketDetailDto>.Ok(await _tickets.EscalateAsync(CurrentActorId, id, request.Note, CurrentIpAddress, ct)));

    [HttpPatch("{id:guid}/assignee")]
    [Authorize(Policy = AuthorizationPolicies.ManagerOrAbove)]
    public async Task<IActionResult> Assign(Guid id, [FromBody] OpsTicketAssignRequest request, CancellationToken ct)
        => Ok(ApiResponse<OpsTicketDetailDto>.Ok(await _tickets.AssignAsync(CurrentActorId, id, request.AssigneeUserId, CurrentIpAddress, ct)));

    [HttpPatch("{id:guid}/priority")]
    [Authorize(Policy = AuthorizationPolicies.ManagerOrAbove)]
    public async Task<IActionResult> SetPriority(Guid id, [FromBody] OpsTicketPriorityRequest request, CancellationToken ct)
        => Ok(ApiResponse<OpsTicketDetailDto>.Ok(await _tickets.SetPriorityAsync(CurrentActorId, id, request.Priority, CurrentIpAddress, ct)));

    [HttpPost("{id:guid}/children")]
    [Authorize(Policy = AuthorizationPolicies.ManagerOrAbove)]
    public async Task<IActionResult> LinkChildren(Guid id, [FromBody] OpsTicketLinkRequest request, CancellationToken ct)
        => Ok(ApiResponse<OpsTicketDetailDto>.Ok(await _tickets.LinkChildrenAsync(CurrentActorId, id, request.TicketIds ?? [], CurrentIpAddress, ct)));

    [HttpDelete("{id:guid}/parent")]
    [Authorize(Policy = AuthorizationPolicies.ManagerOrAbove)]
    public async Task<IActionResult> UnlinkParent(Guid id, CancellationToken ct)
        => Ok(ApiResponse<OpsTicketDetailDto>.Ok(await _tickets.UnlinkParentAsync(CurrentActorId, id, CurrentIpAddress, ct)));

    /// <summary>Trả lời ca cha và mọi ca con chưa đóng. <c>resolve = true</c> thì chuyển chúng sang đã giải quyết.</summary>
    [HttpPost("{id:guid}/broadcast")]
    [Authorize(Policy = AuthorizationPolicies.ManagerOrAbove)]
    public async Task<IActionResult> Broadcast(Guid id, [FromBody] OpsTicketBroadcastRequest request, CancellationToken ct)
        => Ok(ApiResponse<OpsTicketBroadcastResult>.Ok(new OpsTicketBroadcastResult(await _tickets.BroadcastAsync(CurrentActorId, id, request.Body, request.Resolve, CurrentIpAddress, ct))));

    [HttpGet("{id:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> GetAttachment(Guid id, Guid attachmentId, CancellationToken ct)
    {
        var content = await _tickets.GetAttachmentAsync(CurrentActorId, id, attachmentId, CurrentIpAddress, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(content.Data, content.ContentType);
    }
}

public record OpsTicketMessageRequest(string? Body, bool Internal);
public record OpsTicketStatusRequest(SupportTicketStatus Status);
public record OpsTicketEscalateRequest(string? Note);
public record OpsTicketAssignRequest(Guid AssigneeUserId);
public record OpsTicketPriorityRequest(SupportTicketPriority Priority);
public record OpsTicketLinkRequest(IReadOnlyList<Guid>? TicketIds);
public record OpsTicketBroadcastRequest(string? Body, bool Resolve);
public record OpsTicketBroadcastResult(int Count);
