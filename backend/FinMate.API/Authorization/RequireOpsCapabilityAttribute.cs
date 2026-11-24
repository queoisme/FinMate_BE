using FinMate.Application.Ops;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FinMate.API.Authorization;

/// <summary>
/// Chặn endpoint theo quyền (<see cref="OpsCapability"/>) mà Admin chỉnh được trên trang Phân quyền, thay cho việc gắn cứng một vai trò.
/// Quyền được đọc lại từ DB mỗi request (qua <c>IOpsAccessService</c>), nên thu hồi có hiệu lực ngay dù token còn hạn. Thiếu quyền thì 403.
/// Vẫn cần policy đăng nhập nội bộ ở lớp cha (<c>OpsControllerBase</c>): attribute này chỉ thêm kiểm tra quyền.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireOpsCapabilityAttribute(OpsCapability capability) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var userId = context.HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var access = context.HttpContext.RequestServices.GetRequiredService<IOpsAccessService>();
        await access.RequireAsync(Guid.Parse(userId!), capability, context.HttpContext.RequestAborted);
        await next();
    }
}
