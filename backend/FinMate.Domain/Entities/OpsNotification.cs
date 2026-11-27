using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>
/// Một thông báo cho một tài khoản nội bộ (chuông trên web vận hành). Tách khỏi <see cref="UserNotification"/> vì đó là hộp thư app cá nhân:
/// một Admin cũng dùng app như khách, hai hộp thư không được lẫn vào nhau.
/// Tiêu đề và nội dung là câu cố định theo loại, KHÔNG chứa dữ liệu khách (tên, email, số tiền, nội dung ticket) hay chi tiết kỹ thuật.
/// </summary>
public class OpsNotification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public OpsNotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    /// <summary>Loại mục liên quan để web dẫn tới: <c>SupportTicket</c>, <c>OpsApproval</c>, <c>OpsIncident</c>, <c>CommunityPost</c>, <c>CommunityComment</c>, <c>CommunityImage</c>, <c>DataDeletionRequest</c>.</summary>
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Null nghĩa là chưa đọc.</summary>
    public DateTimeOffset? ReadAt { get; set; }

    public User? User { get; set; }
}
