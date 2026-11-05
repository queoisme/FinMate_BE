using System.Security.Claims;
using FinMate.API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Ops;

/// <summary>
/// Lớp cha của mọi controller vận hành. Tách khỏi <c>AdminControllerBase</c> để không phải nới quyền của các API <c>/admin/*</c> khi thêm Staff và OpsManager.
/// Policy nằm ở đây để không controller nào rơi về <c>FallbackPolicy</c>, tức là người dùng thường cũng gọi được.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.StaffOrAbove)]
public abstract class OpsControllerBase : ControllerBase
{
    protected Guid CurrentActorId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>IP để ghi kèm audit log; null khi không xác định được.</summary>
    protected string? CurrentIpAddress => HttpContext.Connection.RemoteIpAddress?.ToString();
}
