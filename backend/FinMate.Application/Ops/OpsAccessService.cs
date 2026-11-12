using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Ops;

public interface IOpsAccessService
{
    /// <summary>Tài khoản nội bộ đang hoạt động, đọc từ DB. Không phải thì 403.</summary>
    Task<User> RequireInternalAsync(Guid actorId, CancellationToken ct = default);

    /// <summary>Như <see cref="RequireInternalAsync"/> và phải có đủ quyền được yêu cầu.</summary>
    Task<User> RequireAsync(Guid actorId, OpsCapability capability, CancellationToken ct = default);

    /// <summary>Vai trò này đang có quyền đó không (đọc DB). Dùng khi cần rẽ nhánh theo quyền thay vì chặn.</summary>
    Task<bool> HasAsync(UserRole role, OpsCapability capability, CancellationToken ct = default);

    /// <summary>Toàn bộ quyền đang áp dụng của một vai trò.</summary>
    Task<OpsCapability> CapabilitiesAsync(UserRole role, CancellationToken ct = default);
}

/// <summary>
/// Cổng kiểm tra cho mọi thao tác nhạy cảm của đội vận hành. Đọc DB mỗi lần vì role trong JWT còn hiệu lực tới 15 phút sau khi
/// bị hạ quyền hoặc khoá, quá lâu với quyền chạm vào tài khoản khách.
/// </summary>
public sealed class OpsAccessService : IOpsAccessService
{
    private const string DeniedMessage = "Bạn không có quyền thực hiện thao tác này.";

    private readonly IUserRepository _users;
    private readonly IRolePermissionService _permissions;

    public OpsAccessService(IUserRepository users, IRolePermissionService permissions)
    {
        _users = users;
        _permissions = permissions;
    }

    public Task<OpsCapability> CapabilitiesAsync(UserRole role, CancellationToken ct = default) => _permissions.ForAsync(role, ct);

    public async Task<bool> HasAsync(UserRole role, OpsCapability capability, CancellationToken ct = default)
        => (await _permissions.ForAsync(role, ct)).HasFlag(capability);

    public async Task<User> RequireInternalAsync(Guid actorId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(actorId, ct);
        if (user is null || user.IsLocked || user.DeletedAt is not null || !user.Role.IsInternal())
        {
            throw new ForbiddenException(DeniedMessage);
        }

        return user;
    }

    public async Task<User> RequireAsync(Guid actorId, OpsCapability capability, CancellationToken ct = default)
    {
        var user = await RequireInternalAsync(actorId, ct);
        if (!await HasAsync(user.Role, capability, ct))
        {
            throw new ForbiddenException(DeniedMessage);
        }

        return user;
    }
}
