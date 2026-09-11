using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

public class SpendingInsight
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public InsightType InsightType { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    public Guid? CategoryId { get; set; }
    public long? AmountCents { get; set; }

    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }

    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public User? User { get; set; }
    public Category? Category { get; set; }
}
