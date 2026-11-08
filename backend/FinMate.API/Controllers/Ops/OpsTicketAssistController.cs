using FinMate.Application.Agent;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Ops;

/// <summary>
/// Gợi ý của AI cho nhân sự khi xử lý ticket (cùng nhóm route với <c>OpsTicketsController</c>). Quyền (StaffOrAbove, xác thực 2 lớp, quyền xem ticket) đi theo <see cref="OpsControllerBase"/> và <c>IOpsTicketService.GetAsync</c>.
/// Chỉ GỢI Ý: không đổi ticket và không gửi gì cho khách. <c>available: false</c> khi trợ lý không dùng được (ticket vẫn xử lý bình thường); <c>verified: false</c> khi câu trả lời gợi ý bị bỏ vì chứa số tiền không có trong ticket.
/// </summary>
[Route("api/v1/ops/tickets")]
public class OpsTicketAssistController(TicketAssistService assist) : OpsControllerBase
{
    [HttpPost("{id:guid}/assist")]
    public async Task<IActionResult> Assist(Guid id, CancellationToken ct)
        => Ok(ApiResponse<TicketAssistDto>.Ok(await assist.AssistAsync(CurrentActorId, id, ct)));
}
