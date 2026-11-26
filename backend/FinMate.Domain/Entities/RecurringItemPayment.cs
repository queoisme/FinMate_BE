using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>
/// Một kỳ đã trả của khoản định kỳ (Phase 92, BR-INS-03). <see cref="TransactionId"/> null = trả tiền mặt không ghi sổ. Gỡ là xoá mềm.
/// Một giao dịch phủ tối đa một dòng còn sống; mỗi kỳ hạn tối đa một dòng không phải tất toán (index duy nhất).
/// </summary>
public class RecurringItemPayment
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid RecurringItemId { get; set; }
    public Guid? TransactionId { get; set; }

    /// <summary>Kỳ hạn mà lần trả này phủ (giờ VN).</summary>
    public DateOnly DueDate { get; set; }

    public RecurringPaymentKind Kind { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public RecurringItem? RecurringItem { get; set; }
    public Transaction? Transaction { get; set; }
}
