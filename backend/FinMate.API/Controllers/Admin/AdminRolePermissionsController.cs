using FinMate.Application.Common.Models;
using FinMate.Application.Ops;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>
/// Admin xem và chỉnh quyền của từng vai trò nội bộ. Kế thừa <see cref="AdminControllerBase"/> nên tự có <c>AdminOnly</c>.
/// Chỉ Staff và OpsManager sửa được; quyền có hiệu lực ngay vì <c>IOpsAccessService</c> đọc DB mỗi lần.
/// </summary>
[Route("api/v1/admin/role-permissions")]
public class AdminRolePermissionsController : AdminControllerBase
{
    private readonly IRolePermissionService _permissions;

    public AdminRolePermissionsController(IRolePermissionService permissions)
    {
        _permissions = permissions;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(ApiResponse<RolePermissionsDto>.Ok(await _permissions.ListAsync(ct)));

    /// <summary>Đặt TRỌN BỘ quyền mong muốn của vai trò (không phải thêm/bớt từng quyền), nên gọi lại cho cùng kết quả.</summary>
    [HttpPut("{role}")]
    public async Task<IActionResult> Save(UserRole role, [FromBody] SaveRolePermissionsRequest request, CancellationToken ct)
        => Ok(ApiResponse<RolePermissionDto>.Ok(await _permissions.SaveAsync(CurrentAdminId, role, request.Capabilities, request.Reason, CurrentIpAddress, ct)));
}

public record SaveRolePermissionsRequest(IReadOnlyList<string>? Capabilities, string? Reason);
