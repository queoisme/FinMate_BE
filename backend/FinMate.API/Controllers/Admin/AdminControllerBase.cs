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
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public abstract class AdminControllerBase : StaffControllerBase
{
}
