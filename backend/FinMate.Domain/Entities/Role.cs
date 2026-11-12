using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>
/// Một vai trò trên toàn hệ thống (bảng <c>roles</c>). <see cref="Id"/> chính là giá trị enum <see cref="UserRole"/> (User=0, Admin=1, Staff=2, OpsManager=3),
/// nên id ổn định và code đọc vai trò không phải tra bảng. Thêm vai trò mới = thêm giá trị ở CUỐI enum và một hàng ở đây.
/// </summary>
public class Role
{
    public UserRole Id { get; set; }

    /// <summary>Tên máy đọc, chữ thường (trước đây là giá trị cột <c>users.role</c>).</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Vai trò mà code phụ thuộc vào (policy, JWT): không xóa hay đổi mã được.</summary>
    public bool IsSystem { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Vai trò đang gán cho một người dùng (bảng <c>user_roles</c>). Khóa chính là <see cref="UserId"/> nên mỗi người đúng MỘT vai trò.
/// </summary>
public class UserRoleAssignment
{
    public Guid UserId { get; set; }
    public UserRole RoleId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }

    /// <summary>Admin đã cấp vai trò này. Null với tài khoản tự đăng ký hoặc do seeder tạo.</summary>
    public Guid? AssignedByUserId { get; set; }
}
