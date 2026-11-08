namespace FinMate.Domain.Enums;

/// <summary>
/// Vai trò của tài khoản trên toàn hệ thống. <see cref="OpsManager"/> là quản lý đội vận hành, khác hẳn vai <c>manager</c> trong một nhóm tài chính (<see cref="GroupRole"/>).
/// Giá trị mới luôn thêm ở cuối vì API nhận cả dạng số.
/// </summary>
public enum UserRole
{
    User,
    Admin,

    /// <summary>Nhân viên hỗ trợ khách hàng.</summary>
    Staff,

    /// <summary>Quản lý đội vận hành: điều phối ticket và duyệt các việc nhạy cảm.</summary>
    OpsManager,
}

public static class UserRoleExtensions
{
    /// <summary>Tài khoản thuộc đội vận hành, làm việc trên web nội bộ.</summary>
    public static bool IsInternal(this UserRole role) => role is UserRole.Staff or UserRole.OpsManager or UserRole.Admin;

    /// <summary>Thứ bậc để nhận biết một lần đổi vai trò có làm giảm quyền hay không. Không dùng thứ tự của enum vì Admin đứng trước Staff.</summary>
    public static int Rank(this UserRole role) => role switch
    {
        UserRole.Admin => 3,
        UserRole.OpsManager => 2,
        UserRole.Staff => 1,
        _ => 0,
    };
}
