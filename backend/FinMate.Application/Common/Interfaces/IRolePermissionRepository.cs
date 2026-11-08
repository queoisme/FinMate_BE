using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

public interface IRolePermissionRepository
{
    Task<RolePermission?> GetAsync(UserRole role, CancellationToken ct = default);

    Task<List<RolePermission>> ListAsync(CancellationToken ct = default);

    /// <summary>Tạo hoặc ghi đè quyền của một vai trò.</summary>
    Task SaveAsync(UserRole role, long capabilities, Guid? updatedByUserId, CancellationToken ct = default);
}
