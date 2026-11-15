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

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }

    public User? User { get; set; }
}
