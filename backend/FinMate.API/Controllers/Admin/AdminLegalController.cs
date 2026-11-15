using FinMate.Application.Common.Models;
using FinMate.Application.Legal;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>
/// Xuất bản Điều khoản và Chính sách quyền riêng tư. Chỉ Admin (kế thừa <see cref="AdminControllerBase"/> nên tự có <c>AdminOnly</c>): xuất bản tạo phiên bản mới
/// và buộc mọi người dùng đồng ý lại. Manager chỉ soạn nháp ở <c>/ops/legal</c>.
/// </summary>
[Route("api/v1/admin/legal")]
public class AdminLegalController : AdminControllerBase
{
    private readonly ILegalAdminService _legal;

    public AdminLegalController(ILegalAdminService legal)
    {
        _legal = legal;
    }

    [HttpPost("draft/publish")]
    public async Task<IActionResult> Publish(CancellationToken ct)
        => Ok(ApiResponse<PublishedLegalVersionDto>.Ok(await _legal.PublishAsync(CurrentAdminId, CurrentIpAddress, ct)));
}
