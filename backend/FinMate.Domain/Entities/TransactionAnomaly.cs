using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>
/// Một khoản chi bị đánh dấu bất thường (Flow 6, trang 9) và phản hồi của user. Mỗi giao dịch có tối đa một dòng (UNIQUE <c>transaction_id</c>),
/// nên một khoản đã xét không bị xét lại. <see cref="Resolution"/> = <see cref="AnomalyResolution.Normal"/> là dữ liệu cho BR-11.4: lần sau
/// không đánh dấu khoản tương tự (cùng danh mục + người nhận, số tiền không lớn hơn hẳn).
/// </summary>
public class TransactionAnomaly
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TransactionId { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CounterpartyKey { get; set; }
    public AnomalyKind Kind { get; set; }
    public AnomalySeverity Severity { get; set; }
    public long AmountCents { get; set; }

    /// <summary>Mức thông thường dùng để so (trung vị danh mục / người nhận; với khoản trùng là số tiền khoản trước).</summary>
    public long TypicalCents { get; set; }

    /// <summary>Số tiền / mức thông thường × 100.</summary>
    public int RatioPercent { get; set; }

    public AnomalyResolution? Resolution { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }
    public Transaction? Transaction { get; set; }
}
