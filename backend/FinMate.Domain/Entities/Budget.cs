using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

public class Budget
{
    public Guid Id { get; set; }
    /// <summary>Chủ cá nhân. Đúng một trong <see cref="UserId"/> và <see cref="GroupId"/> có giá trị (ràng buộc ở CSDL), nên truy vấn theo user không thể thấy ngân sách nhóm.</summary>
    public Guid? UserId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? GroupCategoryId { get; set; }
    public Guid? CreatedByUserId { get; set; }

    /// <summary>NULL = budget tổng chi tiêu (mọi category); khác NULL = budget riêng cho 1 category.</summary>
    public Guid? CategoryId { get; set; }

    public long LimitCents { get; set; }
    public BudgetPeriodType PeriodType { get; set; } = BudgetPeriodType.Monthly;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public User? User { get; set; }
    public Category? Category { get; set; }
    public GroupCategory? GroupCategory { get; set; }
    public ICollection<BudgetPeriod> Periods { get; set; } = new List<BudgetPeriod>();
}
