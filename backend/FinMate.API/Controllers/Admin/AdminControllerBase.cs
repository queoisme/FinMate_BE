using System.Security.Claims;
using FinMate.API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>
/// Lớp cha của mọi controller admin.
///
/// Tồn tại để <c>[Authorize(Policy = AdminOnly)]</c> được kế thừa chứ không phải gõ lại ở
/// từng controller: thiếu attribute đó một lần là endpoint rơi về <c>FallbackPolicy</c>, tức
/// là bất kỳ người dùng đã đăng nhập nào cũng gọi được — và nó vẫn trả 200 nên không có gì
/// báo. Bộ test tích hợp khoá lại hành vi này bằng một test 403 cho từng nhóm endpoint.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public abstract class AdminControllerBase : ControllerBase
{
    protected Guid CurrentAdminId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>IP để ghi kèm audit log; null khi không xác định được (test, proxy nội bộ).</summary>
    protected string? CurrentIpAddress => HttpContext.Connection.RemoteIpAddress?.ToString();
}
