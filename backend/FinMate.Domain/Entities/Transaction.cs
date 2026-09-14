using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

public class Transaction
{
    public Guid Id { get; set; }

    /// <summary>
    /// Id do CLIENT sinh cho mỗi lần tạo, để gửi lại không thành giao dịch trùng.
    ///
    /// Sinh ra cho docx mục "Mạng Offline": app xếp giao dịch vào Room khi mất mạng rồi đồng
    /// bộ khi có mạng trở lại, và một hàng đợi như thế chắc chắn sẽ gửi lại — timeout giữa
    /// chừng, app bị kill, hoặc backoff. Khác thông báo ngân hàng (đã có <c>content_hash</c>
    /// dựng từ nội dung), giao dịch nhập tay không có gì để băm: hai ly cà phê 45.000đ cùng
    /// quán trong cùng một phút là hai giao dịch THẬT, nên chỉ client mới biết đâu là gửi lại.
    ///
    /// NULL được: client cũ không gửi thì vẫn tạo bình thường, chỉ là không có bảo vệ.
    /// </summary>
    public Guid? ClientRequestId { get; set; }
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
