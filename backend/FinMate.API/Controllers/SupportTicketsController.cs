using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.Support;
using FinMate.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>
/// Khiếu nại của CHÍNH người dùng. <c>userId</c> chỉ lấy từ JWT; khiếu nại của người khác luôn là 404. Lỗi 422: <c>invalid_ticket_subject</c>, <c>invalid_ticket_body</c>, <c>ticket_limit_reached</c>;
/// 409 <c>illegal_ticket_transition</c> khi bước không hợp lệ ở trạng thái hiện tại (ví dụ phản đối một khiếu nại chưa được trả lời).
/// </summary>
[ApiController]
[Route("api/v1/support/tickets")]
[Authorize]
public class SupportTicketsController(SupportTicketService service) : ControllerBase
{
    public sealed record CreateTicketRequest(string Subject, string Body);

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTicketRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, ApiResponse<SupportTicketDto>.Ok(await service.CreateAsync(CurrentUserId, request.Subject, request.Body, Ip, ct)));

    /// <summary>Phân trang trên máy chủ: <c>status</c>, <c>limit</c> (1–100, mặc định 20), <c>offset</c>.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] SupportTicketStatus? status, [FromQuery] int limit = 20, [FromQuery] int offset = 0, CancellationToken ct = default)
        => Ok(ApiResponse<SupportTicketPage>.Ok(await service.ListMineAsync(CurrentUserId, status, limit, offset, ct)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(ApiResponse<SupportTicketDto>.Ok(await service.GetMineAsync(CurrentUserId, id, ct)));

    /// <summary>Xác nhận đã được giải quyết (chỉ khi nhân sự đã trả lời).</summary>
    [HttpPost("{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid id, CancellationToken ct)
        => Ok(ApiResponse<SupportTicketDto>.Ok(await service.OwnerTransitionAsync(CurrentUserId, id, SupportTicketStatus.Resolved, Ip, ct)));

    /// <summary>Phản đối kết quả (đã trả lời, đã giải quyết hoặc bị từ chối): khiếu nại mở lại để nhân sự xem lại.</summary>
    [HttpPost("{id:guid}/reopen")]
    public async Task<IActionResult> Reopen(Guid id, CancellationToken ct)
        => Ok(ApiResponse<SupportTicketDto>.Ok(await service.OwnerTransitionAsync(CurrentUserId, id, SupportTicketStatus.Reopened, Ip, ct)));
}
