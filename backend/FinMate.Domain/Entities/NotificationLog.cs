using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

public class NotificationLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? FinancialAccountId { get; set; }
    public string PackageName { get; set; } = string.Empty;
    public string? NotificationTitle { get; set; }
    public string? NotificationBody { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }
    public NotificationLogStatus Status { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }
    public FinancialAccount? FinancialAccount { get; set; }
    public AiResult? AiResult { get; set; }
}
