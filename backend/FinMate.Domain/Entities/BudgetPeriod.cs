namespace FinMate.Domain.Entities;

public class BudgetPeriod
{
    public Guid Id { get; set; }
    public Guid BudgetId { get; set; }

    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }

    /// <summary>Snapshot của Budget.LimitCents lúc period được tạo — đổi limit không làm sai lịch sử.</summary>
    public long LimitCents { get; set; }

    public long SpentCents { get; set; }

    public DateTimeOffset? Alert80SentAt { get; set; }
    public DateTimeOffset? Alert100SentAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Budget? Budget { get; set; }
}
