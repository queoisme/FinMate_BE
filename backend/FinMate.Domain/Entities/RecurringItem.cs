using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>
/// Một khoản thu/chi lặp lại đều đặn: lương, tiền nhà, thuê bao, hóa đơn. Gộp "hóa đơn định kỳ" và "thu nhập đều đặn" vào một thực thể
/// vì chúng có cùng vòng đời (đề xuất → xác nhận → theo dõi → tạm dừng/hủy) và cùng được engine dự báo đặt lên đường số dư theo ngày.
/// </summary>
public class RecurringItem
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public RecurringDirection Direction { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Tên người nhận/người trả đã chuẩn hóa, dùng để khớp giao dịch. Null với khoản user tự thêm không gắn người nhận.</summary>
    public string? CounterpartyKey { get; set; }

    public Guid? CategoryId { get; set; }
    public Guid? FinancialAccountId { get; set; }
    public long AmountCents { get; set; }
    public RecurringCadence Cadence { get; set; }

    /// <summary>Ngày đến hạn kế tiếp (giờ VN). Job đẩy ngày này lên khi thấy giao dịch khớp.</summary>
    public DateOnly NextDueDate { get; set; }

    public RecurringStatus Status { get; set; }
    public RecurringSource Source { get; set; }

    /// <summary>Số lần đã thấy giao dịch khớp.</summary>
    public int Occurrences { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }
    public long? LastSeenAmountCents { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public User? User { get; set; }
    public Category? Category { get; set; }
    public FinancialAccount? FinancialAccount { get; set; }
}
