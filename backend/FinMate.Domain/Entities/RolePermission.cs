using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>
/// Quyền đang áp dụng của một vai trò nội bộ. Một hàng mỗi vai trò nên "đã bỏ hết quyền" khác "chưa cấu hình".
/// <see cref="Capabilities"/> là bitmask của <c>OpsCapability</c> (kiểu enum nằm ở lớp Application nên Domain chỉ giữ số).
/// Admin luôn đủ quyền và User không có quyền nội bộ nên hai vai trò đó không có hàng ở đây.
/// </summary>
public class RolePermission
{
    public UserRole Role { get; set; }
    public long Capabilities { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
