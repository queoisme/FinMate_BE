using FinMate.API.Authorization;
using FinMate.Application.Common.Models;
using FinMate.Application.Legal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Ops;

/// <summary>
/// Manager và Admin soạn nháp Điều khoản và Chính sách quyền riêng tư. Cố ý KHÔNG có <c>ManagerOrAbove</c> như <c>OpsSubscriptionsController</c>: policy đó đọc vai trò trong
/// token nên sẽ chặn Staff mà Admin đã cấp <c>ManageLegalDocuments</c>. Policy cơ sở <c>StaffOrAbove</c> chỉ chặn sớm; quyền thật được đọc lại từ DB trong service.
/// Xuất bản không nằm ở đây mà ở <c>AdminLegalController</c>, vì nó buộc mọi người dùng đồng ý lại.
/// </summary>
[Route("api/v1/ops/legal")]
public class OpsLegalController : OpsControllerBase
{
    private readonly ILegalAdminService _legal;

    public OpsLegalController(ILegalAdminService legal)
    {
        _legal = legal;
    }

    [HttpGet("versions")]
    public async Task<IActionResult> Versions(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<LegalVersionSummaryDto>>.Ok(await _legal.ListVersionsAsync(CurrentActorId, ct)));

    [HttpGet("draft")]
    public async Task<IActionResult> GetDraft(CancellationToken ct)
        => Ok(ApiResponse<LegalDraftDto>.Ok(await _legal.GetDraftAsync(CurrentActorId, ct)));

    /// <summary>Thay toàn bộ nháp. Đã có nháp thì <c>baseUpdatedAt</c> phải là <c>updatedAt</c> client nhận lần trước.</summary>
    [HttpPut("draft")]
    public async Task<IActionResult> SaveDraft([FromBody] SaveLegalDraftInput request, CancellationToken ct)
        => Ok(ApiResponse<LegalDraftDto>.Ok(await _legal.SaveDraftAsync(CurrentActorId, request, CurrentIpAddress, ct)));
}
