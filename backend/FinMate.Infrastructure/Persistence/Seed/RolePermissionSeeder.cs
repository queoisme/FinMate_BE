using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Seed;

/// <summary>
/// Chèn quyền MẶC ĐỊNH (từ <see cref="OpsPermissions"/>) cho Staff và OpsManager khi vai trò đó chưa có hàng.
/// Không bao giờ ghi đè: quyền Admin đã chỉnh trên trang Phân quyền phải sống qua mọi lần khởi động lại.
/// </summary>
public static class RolePermissionSeeder
{
    public static async Task SeedAsync(FinMateDbContext context, CancellationToken ct = default)
    {
        var added = false;
        foreach (var role in RolePermissionRules.EditableRoles)
        {
            if (await context.RolePermissions.AnyAsync(r => r.Role == role, ct))
            {
                continue;
            }

            context.RolePermissions.Add(new RolePermission
            {
                Role = role,
                Capabilities = (long)OpsPermissions.For(role),
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            added = true;
        }

        if (added)
        {
            await context.SaveChangesAsync(ct);
        }
    }
}
