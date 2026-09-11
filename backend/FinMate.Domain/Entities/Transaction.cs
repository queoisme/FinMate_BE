using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

public class Transaction
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FinancialAccountId { get; set; }

    /// <summary>
    /// Ví đích của giao dịch <see cref="TransactionType.Transfer"/> (<see cref="FinancialAccountId"/>
    /// là ví nguồn). NULL với debit/credit — CHECK constraint ràng buộc cả 2 chiều.
    /// </summary>
    public Guid? CounterAccountId { get; set; }

    public Guid? CategoryId { get; set; }
    public Guid? NotificationLogId { get; set; }
    public Guid? SavingGoalId { get; set; }

    public long AmountCents { get; set; }
    public TransactionType TransactionType { get; set; }
    public TransactionSource Source { get; set; }
    public TransactionStatus Status { get; set; }

    public string? MerchantName { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset TransactedAt { get; set; }
    public long? BalanceAfterCents { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public User? User { get; set; }
    public FinancialAccount? FinancialAccount { get; set; }
    public FinancialAccount? CounterAccount { get; set; }
    public Category? Category { get; set; }
    public NotificationLog? NotificationLog { get; set; }
    public SavingGoal? SavingGoal { get; set; }
}
