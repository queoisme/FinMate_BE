using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>Một mục trong hộp thư thông báo trong app (Phân hệ B, màn B2). <see cref="ReadAt"/> null nghĩa là chưa đọc.</summary>
public class UserNotification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public UserNotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    /// <summary>Loại thực thể liên quan để FE deep-link sau này, vd "Transaction", "Budget", "SavingGoal", "RecurringItem".</summary>
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }

    /// <summary>
    /// JSON (object chuỗi→chuỗi) đúng như <c>data</c> của push FCM đã gửi, để app bấm vào mục hộp thư mở cùng màn với khi bấm push.
    /// null: mục không đến từ push. Mang số tiền như body nên cũng KHÔNG BAO GIỜ ghi log.
    /// </summary>
    public string? Data { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }

    public User? User { get; set; }
}
