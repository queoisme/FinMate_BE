using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

public class Budget
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>NULL = budget tổng chi tiêu (mọi category); khác NULL = budget riêng cho 1 category.</summary>
    public Guid? CategoryId { get; set; }

    public long LimitCents { get; set; }
    public BudgetPeriodType PeriodType { get; set; } = BudgetPeriodType.Monthly;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public User? User { get; set; }
    public Category? Category { get; set; }
    public ICollection<BudgetPeriod> Periods { get; set; } = new List<BudgetPeriod>();
}
