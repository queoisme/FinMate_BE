using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>Nhóm hoặc gia đình cùng theo dõi chi tiêu. Chủ nhóm nằm ở <see cref="GroupMember"/>, không lưu ở đây, để chỉ có một nơi quyết định vai trò.</summary>
public class Group
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public GroupType Type { get; set; } = GroupType.Family;
    public GroupTransactionVisibility TransactionVisibility { get; set; } = GroupTransactionVisibility.Full;
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>
/// Tư cách thành viên. Rời nhóm chỉ đặt <see cref="LeftAt"/>, không xóa dòng: lịch sử của nhóm vẫn cần biết người đó là ai.
/// Vào lại thì dùng lại chính dòng này.
/// </summary>
public class GroupMember
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public GroupRole Role { get; set; } = GroupRole.Member;
    public GroupPermission PermissionsGranted { get; set; }
    public GroupPermission PermissionsRevoked { get; set; }
    public bool AlertsEnabled { get; set; } = true;
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Group? Group { get; set; }
    public User? User { get; set; }
}

/// <summary>Lời mời dùng một lần. Chỉ giữ SHA-256 của mã: DB bị lộ cũng không dùng được mã để vào nhóm.</summary>
public class GroupInvitation
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Có giá trị thì chỉ tài khoản đã xác minh đúng email này nhận được; null là lời mời qua link.</summary>
    public string? Email { get; set; }

    public GroupRole Role { get; set; } = GroupRole.Member;
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? AcceptedByUserId { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public bool IsPending(DateTimeOffset now) => AcceptedAt is null && RevokedAt is null && ExpiresAt > now;
}

/// <summary>Danh mục riêng của nhóm. Bảng riêng, không dùng <c>categories</c>: ở đó <c>user_id IS NULL</c> nghĩa là danh mục hệ thống hiện cho mọi người.</summary>
public class GroupCategory
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? IconName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Quy tắc tự chia sẻ của MỘT người: giao dịch khớp ví và danh mục sẽ tự vào nhóm. Bỏ trống cả hai là chia sẻ mọi khoản của người đó.</summary>
public class GroupShareRule
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public Guid? FinancialAccountId { get; set; }
    public Guid? CategoryId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Lịch sử cho thành viên xem trong app. Khác <c>audit_logs</c> (cho vận hành): dòng này thuộc về nhóm.</summary>
public class GroupActivity
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid? ActorUserId { get; set; }
    public GroupActivityType Type { get; set; }
    public Guid? SubjectId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
