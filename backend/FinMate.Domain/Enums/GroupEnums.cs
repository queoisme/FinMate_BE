namespace FinMate.Domain.Enums;

public enum GroupType
{
    Family,
    Other,
}

/// <summary>Thứ tự khai báo là thứ bậc: giá trị nhỏ hơn có quyền lớn hơn. So sánh vai trò dựa vào điều này.</summary>
public enum GroupRole
{
    Owner,
    Manager,
    Member,
    Viewer,
}

/// <summary>Thành viên thường và người chỉ xem có được thấy từng giao dịch đã chia sẻ không, hay chỉ thấy tổng.</summary>
public enum GroupTransactionVisibility
{
    Full,
    Summary,
}

/// <summary>
/// Quyền cấp được cho từng người. Việc chỉ chủ nhóm mới làm được (đổi vai trò, chỉnh quyền, đổi cài đặt, chuyển chủ, xóa nhóm) cố ý
/// KHÔNG có ở đây, nên không ai có thể được cấp. Giá trị được lưu trong DB: không đổi số của quyền đã có.
/// </summary>
[Flags]
public enum GroupPermission
{
    None = 0,
    ViewOverview = 1,
    ViewMemberTransactions = 2,
    ViewActivity = 4,
    ShareTransactions = 8,
    ContributeGoals = 16,
    ManageSharedTransactions = 32,
    ManageBudgets = 64,
    ManageGoals = 128,
    ManageCategories = 256,
    InviteMembers = 512,
    RemoveMembers = 1024,
}

public enum GroupActivityType
{
    GroupCreated,
    GroupUpdated,
    MemberJoined,
    MemberLeft,
    MemberRemoved,
    MemberUpdated,
    OwnershipTransferred,
    InvitationCreated,
    InvitationRevoked,
}
