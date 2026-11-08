using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;

namespace FinMate.Application.Groups;

/// <summary>Đội vận hành đổi chủ nhóm sau khi đề xuất đã được duyệt, khi chủ nhóm lạm quyền hoặc không còn liên lạc được.</summary>
public interface IGroupSupportTransfer
{
    Task TransferAsync(Guid actorId, Guid groupId, Guid newOwnerUserId, string? ip, CancellationToken ct = default);
}

public sealed class GroupSupportTransfer : IGroupSupportTransfer
{
    private readonly IGroupRepository _groups;
    private readonly IPushNotificationService _push;
    private readonly IAuditLogService _audit;

    public GroupSupportTransfer(IGroupRepository groups, IPushNotificationService push, IAuditLogService audit)
    {
        _groups = groups;
        _push = push;
        _audit = audit;
    }

    public async Task TransferAsync(Guid actorId, Guid groupId, Guid newOwnerUserId, string? ip, CancellationToken ct = default)
    {
        var members = await _groups.ListActiveMembersAsync(groupId, ct);
        var newOwner = await _groups.GetActiveMemberAsync(groupId, newOwnerUserId, ct)
            ?? throw new NotFoundException("GroupMember", newOwnerUserId);

        // Actor để trống: nhân viên không phải thành viên nên không hiện tên họ trong lịch sử của nhóm.
        _groups.AddActivity(GroupService.NewActivity(groupId, null, GroupActivityType.OwnershipTransferred, newOwnerUserId, DateTimeOffset.UtcNow));

        var currentOwnerId = members.FirstOrDefault(m => m.Member.Role == GroupRole.Owner)?.Member.UserId;
        if (currentOwnerId is { } ownerId && await _groups.GetActiveMemberAsync(groupId, ownerId, ct) is { } currentOwner)
        {
            await _groups.TransferOwnershipAsync(currentOwner, newOwner, ct);
        }
        else
        {
            newOwner.Role = GroupRole.Owner;
            newOwner.PermissionsGranted = GroupPermission.None;
            newOwner.PermissionsRevoked = GroupPermission.None;
            newOwner.UpdatedAt = DateTimeOffset.UtcNow;
            await _groups.SaveChangesAsync(ct);
        }

        await _audit.LogAsync(AuditEvents.OpsGroupOwnerTransferred, actorId, ip, new { groupId, previousOwnerUserId = currentOwnerId, newOwnerUserId }, ct);

        // Mọi thành viên đều phải biết nhóm vừa đổi chủ do hỗ trợ can thiệp.
        foreach (var member in members)
        {
            await _push.NotifyAsync(
                member.Member.UserId,
                "Nhóm vừa đổi chủ",
                "Đội hỗ trợ FinMate đã chuyển quyền chủ nhóm theo yêu cầu được xác minh.",
                new Dictionary<string, string> { ["action"] = "group_owner_changed", ["groupId"] = groupId.ToString() },
                ct);
        }
    }
}
