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

    // 3 mức cảnh báo theo Core Flow 3 (ARCHITECTURE.md §0 quyết định #2). Mỗi mức là một dấu
    // vết riêng vì mỗi mức chỉ được bắn đúng 1 lần trong 1 chu kỳ. Đặt tên theo MỨC chứ không theo
    // phần trăm: T1 và T2 do người dùng chỉnh (mặc định 70 và 90), chỉ mức "vượt hạn mức" cố định ở 100%.
    public DateTimeOffset? AlertT1SentAt { get; set; }
    public DateTimeOffset? AlertT2SentAt { get; set; }
    public DateTimeOffset? AlertOverSentAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Budget? Budget { get; set; }
}
