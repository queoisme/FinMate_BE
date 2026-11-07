using FinMate.API.Authorization;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Models;
using FinMate.Application.Support;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>
/// Hàng đợi khiếu nại cho nhân sự (SupportStaff trở lên). Mỗi bước đi qua bảng chuyển trạng thái; bước của quản lý (<c>review</c>, <c>decide</c>) có thêm policy ManagerOrAbove và cũng bị chặn ở tầng dịch vụ.
/// <c>assist</c> chỉ GỢI Ý (tóm tắt + bản nháp trả lời): không đổi khiếu nại, không gửi gì cho người dùng; câu trả lời chỉ ra ngoài khi nhân sự gọi <c>reply</c>.
/// 404 không có, 409 <c>illegal_ticket_transition</c>, 403 sai vai trò cho bước đó, 422 <c>invalid_ticket_reply</c> / <c>invalid_ticket_note</c>.
/// </summary>
[Route("api/v1/admin/support/tickets")]
public class AdminSupportController(SupportTicketService service) : SupportStaffControllerBase
{
    public sealed record ReplyRequest(string Text);

    public sealed record DecideRequest(string Outcome, string Note);

    private UserRole CurrentRole => Enum.Parse<UserRole>(User.FindFirst(System.Security.Claims.ClaimTypes.Role)!.Value);

    private Task<SupportTicketDto> MoveAsync(Guid id, SupportTicketStatus to, string? reply, string? note, CancellationToken ct)
        => service.StaffTransitionAsync(CurrentAdminId, CurrentRole, id, to, reply, note, CurrentIpAddress, ct);

    /// <summary>Phân trang và tìm kiếm trên máy chủ: <c>status</c>, <c>search</c> (tiêu đề), <c>limit</c> (1–100), <c>offset</c>.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] SupportTicketStatus? status, [FromQuery] string? search, [FromQuery] int limit = 20, [FromQuery] int offset = 0, CancellationToken ct = default)
        => Ok(ApiResponse<SupportTicketPage>.Ok(await service.StaffListAsync(status, search, limit, offset, ct)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(ApiResponse<SupportTicketDto>.Ok(await service.StaffGetAsync(id, ct)));

    /// <summary>Open / Reopened → Triaged.</summary>
    [HttpPost("{id:guid}/triage")]
    public async Task<IActionResult> Triage(Guid id, CancellationToken ct)
        => Ok(ApiResponse<SupportTicketDto>.Ok(await MoveAsync(id, SupportTicketStatus.Triaged, null, null, ct)));

    /// <summary>Gửi câu trả lời cho người dùng: Triaged → Responded.</summary>
    [HttpPost("{id:guid}/reply")]
    public async Task<IActionResult> Reply(Guid id, [FromBody] ReplyRequest request, CancellationToken ct)
        => Ok(ApiResponse<SupportTicketDto>.Ok(await MoveAsync(id, SupportTicketStatus.Responded, request.Text, null, ct)));

    /// <summary>Leo thang lên quản lý: Triaged → Escalated.</summary>
    [HttpPost("{id:guid}/escalate")]
    public async Task<IActionResult> Escalate(Guid id, CancellationToken ct)
        => Ok(ApiResponse<SupportTicketDto>.Ok(await MoveAsync(id, SupportTicketStatus.Escalated, null, null, ct)));

    /// <summary>Đóng khiếu nại đã trả lời: Responded → Resolved.</summary>
    [HttpPost("{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid id, CancellationToken ct)
        => Ok(ApiResponse<SupportTicketDto>.Ok(await MoveAsync(id, SupportTicketStatus.Resolved, null, null, ct)));

    /// <summary>Quản lý nhận xử lý: Escalated → ManagerReview.</summary>
    [HttpPost("{id:guid}/review")]
    [Authorize(Policy = AuthorizationPolicies.ManagerOrAbove)]
    public async Task<IActionResult> Review(Guid id, CancellationToken ct)
        => Ok(ApiResponse<SupportTicketDto>.Ok(await MoveAsync(id, SupportTicketStatus.ManagerReview, null, null, ct)));

    /// <summary>Quyết định cuối: <c>{"outcome": "resolved" | "rejected", "note": "..."}</c> (ghi chú 3–500 ký tự).</summary>
    [HttpPost("{id:guid}/decide")]
    [Authorize(Policy = AuthorizationPolicies.ManagerOrAbove)]
    public async Task<IActionResult> Decide(Guid id, [FromBody] DecideRequest request, CancellationToken ct)
    {
        var to = request.Outcome?.ToLowerInvariant() switch
        {
            "resolved" => SupportTicketStatus.Resolved,
            "rejected" => SupportTicketStatus.Rejected,
            _ => throw new BusinessRuleException("invalid_ticket_outcome", "Kết quả phải là 'resolved' hoặc 'rejected'."),
        };
        return Ok(ApiResponse<SupportTicketDto>.Ok(await MoveAsync(id, to, null, request.Note, ct)));
    }

    /// <summary>Gợi ý cho nhân sự. <c>available: false</c> khi trợ lý không dùng được (khiếu nại vẫn xử lý bình thường); <c>verified: false</c> khi câu trả lời gợi ý bị bỏ vì chứa số tiền không có trong khiếu nại.</summary>
    [HttpPost("{id:guid}/assist")]
    public async Task<IActionResult> Assist(Guid id, CancellationToken ct)
        => Ok(ApiResponse<TicketAssistDto>.Ok(await service.AssistAsync(id, ct)));
}
