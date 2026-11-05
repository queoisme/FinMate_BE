using FinMate.Domain.Enums;

namespace FinMate.Application.Groups;

/// <summary>Tính quyền hiệu lực của một thành viên. Thuần: một nơi duy nhất quyết định ai được làm gì.</summary>
public static class GroupPermissions
{
    public static readonly GroupPermission All = Enum.GetValues<GroupPermission>().Aggregate(GroupPermission.None, (all, p) => all | p);

    private const GroupPermission MemberDefaults =
        GroupPermission.ViewOverview | GroupPermission.ViewActivity | GroupPermission.ShareTransactions | GroupPermission.ContributeGoals;

    private const GroupPermission ViewerDefaults = GroupPermission.ViewOverview;

    /// <summary>Quyền ghi đè không áp cho chủ nhóm: nhóm không thể rơi vào cảnh không ai quản lý được.</summary>
    public static GroupPermission Effective(
        GroupRole role,
        GroupTransactionVisibility visibility,
        GroupPermission granted,
        GroupPermission revoked)
        => role == GroupRole.Owner ? All : (RoleDefaults(role, visibility) | granted) & ~revoked;

    public static GroupPermission RoleDefaults(GroupRole role, GroupTransactionVisibility visibility) => role switch
    {
        GroupRole.Owner or GroupRole.Manager => All,
        GroupRole.Member => MemberDefaults | SharedDetails(visibility),
        GroupRole.Viewer => ViewerDefaults | SharedDetails(visibility),
        _ => GroupPermission.None,
    };

    public static IReadOnlyList<GroupPermission> Expand(GroupPermission permissions)
        => Enum.GetValues<GroupPermission>().Where(p => p != GroupPermission.None && permissions.HasFlag(p)).ToList();

    public static GroupPermission Combine(IEnumerable<GroupPermission> permissions)
        => permissions.Aggregate(GroupPermission.None, (all, p) => all | p) & All;   // client gửi số lạ thì bỏ các bit không phải quyền

    /// <summary>Vai trò <paramref name="actor"/> có cao hơn hẳn <paramref name="target"/> không. Quản lý chỉ tác động lên vai trò thấp hơn mình.</summary>
    public static bool Outranks(GroupRole actor, GroupRole target) => actor < target;

    private static GroupPermission SharedDetails(GroupTransactionVisibility visibility)
        => visibility == GroupTransactionVisibility.Full ? GroupPermission.ViewMemberTransactions : GroupPermission.None;
}
