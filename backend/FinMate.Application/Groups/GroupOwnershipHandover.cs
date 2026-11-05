using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;

namespace FinMate.Application.Groups;

/// <summary>
/// Người yêu cầu xóa tài khoản không đăng nhập được nữa, nên nhóm họ làm chủ phải có chủ mới ngay lúc đó. Không chặn yêu cầu xóa: đó là quyền
/// của họ. Admin hủy yêu cầu thì họ vẫn là thành viên nhưng không được trả lại quyền chủ, vì nhóm đã có chủ mới.
/// </summary>
public interface IGroupOwnershipHandover
{
    Task HandOverAsync(Guid userId, CancellationToken ct = default);
}

public class GroupOwnershipHandover : IGroupOwnershipHandover
{
    private readonly IGroupRepository _groups;
    private readonly IAuditLogService _audit;

    public GroupOwnershipHandover(IGroupRepository groups, IAuditLogService audit)
    {
        _groups = groups;
        _audit = audit;
    }

    public async Task HandOverAsync(Guid userId, CancellationToken ct = default)
    {
        foreach (var groupId in await _groups.ListOwnedGroupIdsAsync(userId, ct))
        {
            var members = await _groups.ListActiveMembersAsync(groupId, ct);
            var owner = members.Single(m => m.Member.UserId == userId).Member;
            var successor = members
                .Where(m => m.Member.UserId != userId && m.IsUserActive)
                .OrderBy(m => m.Member.Role)
                .ThenBy(m => m.Member.JoinedAt)
                .FirstOrDefault();

            // Không còn ai để giao: nhóm chờ tới lúc xóa cứng, khi đó nó bị xóa mềm hoặc người còn lại được nâng lên.
            if (successor is null)
            {
                continue;
            }

            _groups.AddActivity(GroupService.NewActivity(groupId, userId, GroupActivityType.OwnershipTransferred, successor.Member.UserId, DateTimeOffset.UtcNow));
            await _groups.TransferOwnershipAsync(owner, successor.Member, ct);
            await _audit.LogAsync(AuditEvents.GroupOwnershipTransferred, userId, metadata: new { GroupId = groupId, NewOwnerUserId = successor.Member.UserId, Reason = "AccountDeletionRequested" }, ct: ct);
        }
    }
}
