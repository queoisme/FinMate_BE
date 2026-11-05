using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Groups;

/// <param name="Permissions">Chỉ có với chính mình hoặc khi người xem là chủ nhóm; người khác không cần biết quyền của nhau.</param>
public sealed record GroupMemberDto(
    Guid UserId,
    string DisplayName,
    bool IsActive,
    GroupRole Role,
    DateTimeOffset JoinedAt,
    IReadOnlyList<GroupPermission>? Permissions,
    IReadOnlyList<GroupPermission>? GrantedPermissions,
    IReadOnlyList<GroupPermission>? RevokedPermissions);

/// <param name="Role">Null = giữ nguyên. Đổi vai trò thì quyền ghi đè cũ bị xóa, trừ khi truyền kèm quyền mới.</param>
public sealed record UpdateGroupMemberCommand(
    Guid UserId,
    Guid GroupId,
    Guid TargetUserId,
    GroupRole? Role,
    IReadOnlyList<GroupPermission>? GrantedPermissions,
    IReadOnlyList<GroupPermission>? RevokedPermissions);

public interface IGroupMemberService
{
    Task<IReadOnlyList<GroupMemberDto>> ListAsync(Guid groupId, Guid userId, CancellationToken ct = default);
    Task<GroupMemberDto> UpdateAsync(UpdateGroupMemberCommand command, CancellationToken ct = default);
    Task RemoveAsync(Guid groupId, Guid userId, Guid targetUserId, CancellationToken ct = default);
    Task LeaveAsync(Guid groupId, Guid userId, CancellationToken ct = default);
    Task TransferOwnershipAsync(Guid groupId, Guid userId, Guid newOwnerUserId, CancellationToken ct = default);
}

public class GroupMemberService : IGroupMemberService
{
    /// <summary>Tên hiển thị thay cho người đã yêu cầu xóa tài khoản: họ đã rút khỏi hệ thống, tên không nên còn hiện ra.</summary>
    internal const string InactiveMemberName = "Thành viên không còn hoạt động";

    private readonly IGroupRepository _groups;
    private readonly IGroupAccessService _access;
    private readonly IAuditLogService _audit;

    public GroupMemberService(IGroupRepository groups, IGroupAccessService access, IAuditLogService audit)
    {
        _groups = groups;
        _access = access;
        _audit = audit;
    }

    public async Task<IReadOnlyList<GroupMemberDto>> ListAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var access = await _access.RequireAsync(groupId, userId, GroupPermission.ViewOverview, ct);
        var members = await _groups.ListActiveMembersAsync(groupId, ct);
        return members.Select(m => ToDto(m, access, userId)).ToList();
    }

    public async Task<GroupMemberDto> UpdateAsync(UpdateGroupMemberCommand command, CancellationToken ct = default)
    {
        var access = await _access.RequireOwnerAsync(command.GroupId, command.UserId, ct);
        if (command.TargetUserId == command.UserId)
        {
            throw new BusinessRuleException(GroupErrorCodes.CannotChangeSelf, "Không thể tự đổi vai trò hoặc quyền của chính mình.");
        }

        if (command.Role == GroupRole.Owner)
        {
            throw new BusinessRuleException(GroupErrorCodes.InvalidOwnerAssignment, "Dùng chức năng chuyển chủ nhóm để đổi chủ.");
        }

        var target = await RequireActiveTargetAsync(command.GroupId, command.TargetUserId, ct);
        ApplyChanges(target, command);
        var now = DateTimeOffset.UtcNow;
        target.UpdatedAt = now;

        _groups.AddActivity(GroupService.NewActivity(command.GroupId, command.UserId, GroupActivityType.MemberUpdated, command.TargetUserId, now));
        await _groups.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.GroupMemberUpdated, command.UserId, metadata: new { command.GroupId, command.TargetUserId, Role = target.Role.ToString() }, ct: ct);

        var view = (await _groups.ListActiveMembersAsync(command.GroupId, ct)).Single(m => m.Member.UserId == command.TargetUserId);
        return ToDto(view, access, command.UserId);
    }

    public async Task RemoveAsync(Guid groupId, Guid userId, Guid targetUserId, CancellationToken ct = default)
    {
        var access = await _access.RequireAsync(groupId, userId, GroupPermission.RemoveMembers, ct);
        if (targetUserId == userId)
        {
            throw new BusinessRuleException(GroupErrorCodes.CannotChangeSelf, "Dùng chức năng rời nhóm để tự rời khỏi nhóm.");
        }

        var target = await RequireActiveTargetAsync(groupId, targetUserId, ct);
        if (!GroupPermissions.Outranks(access.Member.Role, target.Role))
        {
            throw new ForbiddenException("Bạn chỉ xóa được thành viên có vai trò thấp hơn mình.");
        }

        var now = DateTimeOffset.UtcNow;
        target.LeftAt = now;
        target.UpdatedAt = now;
        _groups.AddActivity(GroupService.NewActivity(groupId, userId, GroupActivityType.MemberRemoved, targetUserId, now));
        await _groups.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.GroupMemberRemoved, userId, metadata: new { GroupId = groupId, TargetUserId = targetUserId }, ct: ct);
    }

    public async Task LeaveAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var access = await _access.RequireMemberAsync(groupId, userId, ct);
        if (access.Member.Role == GroupRole.Owner)
        {
            throw new BusinessRuleException(GroupErrorCodes.OwnerMustTransfer, "Chủ nhóm cần chuyển quyền chủ nhóm cho người khác, hoặc xóa nhóm, trước khi rời.");
        }

        var now = DateTimeOffset.UtcNow;
        access.Member.LeftAt = now;
        access.Member.UpdatedAt = now;
        _groups.AddActivity(GroupService.NewActivity(groupId, userId, GroupActivityType.MemberLeft, userId, now));
        await _groups.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.GroupMemberLeft, userId, metadata: new { GroupId = groupId }, ct: ct);
    }

    public async Task TransferOwnershipAsync(Guid groupId, Guid userId, Guid newOwnerUserId, CancellationToken ct = default)
    {
        var access = await _access.RequireOwnerAsync(groupId, userId, ct);
        if (newOwnerUserId == userId)
        {
            throw new BusinessRuleException(GroupErrorCodes.InvalidOwnerAssignment, "Bạn đang là chủ nhóm.");
        }

        var newOwner = await RequireActiveTargetAsync(groupId, newOwnerUserId, ct);
        _groups.AddActivity(GroupService.NewActivity(groupId, userId, GroupActivityType.OwnershipTransferred, newOwnerUserId, DateTimeOffset.UtcNow));
        await _groups.TransferOwnershipAsync(access.Member, newOwner, ct);
        await _audit.LogAsync(AuditEvents.GroupOwnershipTransferred, userId, metadata: new { GroupId = groupId, NewOwnerUserId = newOwnerUserId }, ct: ct);
    }

    private static void ApplyChanges(GroupMember target, UpdateGroupMemberCommand command)
    {
        if (command.Role is { } role && role != target.Role)
        {
            target.Role = role;
            target.PermissionsGranted = GroupPermission.None;
            target.PermissionsRevoked = GroupPermission.None;
        }

        if (command.GrantedPermissions is not null)
        {
            target.PermissionsGranted = GroupPermissions.Combine(command.GrantedPermissions);
        }

        if (command.RevokedPermissions is not null)
        {
            target.PermissionsRevoked = GroupPermissions.Combine(command.RevokedPermissions);
        }
    }

    private async Task<GroupMember> RequireActiveTargetAsync(Guid groupId, Guid targetUserId, CancellationToken ct)
        => await _groups.GetActiveMemberAsync(groupId, targetUserId, ct) ?? throw new NotFoundException("GroupMember", targetUserId);

    private static GroupMemberDto ToDto(GroupMemberView view, GroupAccess viewer, Guid viewerUserId)
    {
        var member = view.Member;
        var showPermissions = member.UserId == viewerUserId || viewer.Member.Role == GroupRole.Owner;
        return new GroupMemberDto(
            member.UserId,
            view.IsUserActive ? view.DisplayName : InactiveMemberName,
            view.IsUserActive,
            member.Role,
            member.JoinedAt,
            showPermissions ? GroupPermissions.Expand(GroupPermissions.Effective(member.Role, viewer.Group.TransactionVisibility, member.PermissionsGranted, member.PermissionsRevoked)) : null,
            showPermissions ? GroupPermissions.Expand(member.PermissionsGranted) : null,
            showPermissions ? GroupPermissions.Expand(member.PermissionsRevoked) : null);
    }
}
