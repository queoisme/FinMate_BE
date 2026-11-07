using System.Security.Claims;
using FinMate.API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>
/// Thành viên chung của mọi controller dành cho nhân sự vận hành. Không mang policy: mỗi lớp con bên dưới (và <see cref="AdminControllerBase"/>) gắn đúng MỘT policy ở lớp cơ sở,
/// cùng quy ước "policy ở lớp cha, không gõ lại từng controller" để một controller mới không thể quên attribute và rơi về <c>FallbackPolicy</c> (mọi người dùng đăng nhập).
/// Hành động cần quyền CAO HƠN controller của nó thì thêm <c>[Authorize(Policy = ManagerOrAbove)]</c> ở chính hành động đó (hai policy cùng phải qua); test RBAC khóa từng hành động.
/// </summary>
[ApiController]
public abstract class StaffControllerBase : ControllerBase
{
    protected Guid CurrentAdminId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>IP để ghi kèm audit log; null khi không xác định được (test, proxy nội bộ).</summary>
    protected string? CurrentIpAddress => HttpContext.Connection.RemoteIpAddress?.ToString();
}

/// <summary>Nhân sự nội dung (tri thức, giả định kinh tế) trở lên: ContentStaff, Manager, Admin.</summary>
[Authorize(Policy = AuthorizationPolicies.ContentStaffOrAbove)]
public abstract class ContentStaffControllerBase : StaffControllerBase
{
}

/// <summary>Nhân sự hỗ trợ khách hàng trở lên: SupportStaff, Manager, Admin.</summary>
[Authorize(Policy = AuthorizationPolicies.SupportStaffOrAbove)]
public abstract class SupportStaffControllerBase : StaffControllerBase
{
}

/// <summary>Quản lý trở lên: Manager, Admin.</summary>
[Authorize(Policy = AuthorizationPolicies.ManagerOrAbove)]
public abstract class ManagerControllerBase : StaffControllerBase
{
}
