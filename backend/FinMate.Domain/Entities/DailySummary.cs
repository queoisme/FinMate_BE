namespace FinMate.Domain.Entities;

/// <summary>
/// Aggregate dựng sẵn theo ngày (giờ VN) — AGENTS.md §3: summary không được tính lại từ toàn
/// bộ transaction mỗi lần đọc. <see cref="SummaryDate"/> là <c>DateOnly</c> nên không dính
/// bẫy offset của <c>timestamptz</c>.
/// </summary>
public class DailySummary
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateOnly SummaryDate { get; set; }

    public long TotalSpentCents { get; set; }
    public long TotalIncomeCents { get; set; }
    public int TransactionCount { get; set; }

    public Guid? TopCategoryId { get; set; }
    public long TopCategorySpentCents { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User? User { get; set; }
    public Category? TopCategory { get; set; }
}
