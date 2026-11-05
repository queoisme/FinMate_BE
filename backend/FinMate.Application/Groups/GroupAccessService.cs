using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Groups;

public sealed record GroupAccess(Group Group, GroupMember Member, GroupPermission Permissions)
{
    public bool Has(GroupPermission permission) => Permissions.HasFlag(permission);
}

/// <summary>
/// Cổng duy nhất vào dữ liệu nhóm. Đọc DB mỗi lần, không đưa vào JWT: người bị xóa khỏi nhóm phải mất quyền ngay ở request kế tiếp.
/// Không phải thành viên thì 404 (không để lộ nhóm có tồn tại); là thành viên nhưng thiếu quyền thì 403.
/// </summary>
public interface IGroupAccessService
{
    Task<GroupAccess> RequireMemberAsync(Guid groupId, Guid userId, CancellationToken ct = default);
    Task<GroupAccess> RequireAsync(Guid groupId, Guid userId, GroupPermission permission, CancellationToken ct = default);
    Task<GroupAccess> RequireOwnerAsync(Guid groupId, Guid userId, CancellationToken ct = default);
}

public class GroupAccessService : IGroupAccessService
{
    private readonly IGroupRepository _groups;

    public GroupAccessService(IGroupRepository groups)
    {
        _groups = groups;
    }

    public async Task<GroupAccess> RequireMemberAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var group = await _groups.GetByIdAsync(groupId, ct) ?? throw new NotFoundException("Group", groupId);
        var member = await _groups.GetActiveMemberAsync(groupId, userId, ct) ?? throw new NotFoundException("Group", groupId);
        var permissions = GroupPermissions.Effective(member.Role, group.TransactionVisibility, member.PermissionsGranted, member.PermissionsRevoked);
        return new GroupAccess(group, member, permissions);
    }

    public async Task<GroupAccess> RequireAsync(Guid groupId, Guid userId, GroupPermission permission, CancellationToken ct = default)
    {
        var access = await RequireMemberAsync(groupId, userId, ct);
        return access.Has(permission) ? access : throw new ForbiddenException("Bạn không có quyền thực hiện thao tác này trong nhóm.");
    }

    public async Task<GroupAccess> RequireOwnerAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var access = await RequireMemberAsync(groupId, userId, ct);
        return access.Member.Role == GroupRole.Owner ? access : throw new ForbiddenException("Chỉ chủ nhóm mới thực hiện được thao tác này.");
    }
}
