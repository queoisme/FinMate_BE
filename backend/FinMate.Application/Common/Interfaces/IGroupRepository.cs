using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

/// <param name="IsUserActive">False khi tài khoản đã yêu cầu xóa; tên không được hiển thị nữa.</param>
public sealed record GroupMemberView(GroupMember Member, string DisplayName, bool IsUserActive);

public sealed record GroupWithMembership(Group Group, GroupMember Member, int MemberCount);

/// <summary>Truy cập nhóm cố ý nằm tách khỏi các repository theo <c>userId</c>: quyền được kiểm ở <c>IGroupAccessService</c> trước khi gọi vào đây.</summary>
public interface IGroupRepository
{
    Task<Group?> GetByIdAsync(Guid groupId, CancellationToken ct = default);
    Task<GroupMember?> GetActiveMemberAsync(Guid groupId, Guid userId, CancellationToken ct = default);

    /// <summary>Gồm cả người đã rời, để vào lại dùng lại đúng dòng cũ.</summary>
    Task<GroupMember?> GetMemberAsync(Guid groupId, Guid userId, CancellationToken ct = default);

    Task<List<GroupWithMembership>> ListForUserAsync(Guid userId, CancellationToken ct = default);
    Task<List<GroupMemberView>> ListActiveMembersAsync(Guid groupId, CancellationToken ct = default);
    Task<int> CountActiveMembersAsync(Guid groupId, CancellationToken ct = default);
    Task<int> CountGroupsForUserAsync(Guid userId, CancellationToken ct = default);

    void AddGroup(Group group);
    void AddMember(GroupMember member);
    void AddActivity(GroupActivity activity);
    void AddInvitation(GroupInvitation invitation);

    Task<GroupInvitation?> GetInvitationAsync(Guid groupId, Guid invitationId, CancellationToken ct = default);
    Task<GroupInvitation?> GetInvitationByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task<List<GroupInvitation>> ListPendingInvitationsAsync(Guid groupId, DateTimeOffset now, CancellationToken ct = default);
    Task<int> CountPendingInvitationsAsync(Guid groupId, DateTimeOffset now, CancellationToken ct = default);

    Task<List<GroupActivity>> ListActivitiesAsync(Guid groupId, int limit, CancellationToken ct = default);

    /// <summary>Hạ chủ cũ và nâng chủ mới trong một DB transaction, theo đúng thứ tự: chỉ mục "một chủ" không cho phép có hai chủ dù chỉ một câu lệnh.</summary>
    Task TransferOwnershipAsync(GroupMember currentOwner, GroupMember newOwner, CancellationToken ct = default);

    /// <summary>Lưu mọi thay đổi đang theo dõi. Hai thao tác đồng thời đụng ràng buộc thành viên thì ném 409, không phải 500.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}
