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

    // 3 mốc cảnh báo theo Core Flow 3 (ARCHITECTURE.md §0 quyết định #2). Mỗi mốc là một dấu
    // vết riêng vì mỗi mốc chỉ được bắn đúng 1 lần trong 1 chu kỳ.
    public DateTimeOffset? Alert70SentAt { get; set; }
    public DateTimeOffset? Alert90SentAt { get; set; }
    public DateTimeOffset? Alert100SentAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Budget? Budget { get; set; }
}
