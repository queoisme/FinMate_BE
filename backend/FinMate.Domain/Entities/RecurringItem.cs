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

    /// <summary>Tổng số kỳ phải trả (trả góp), 1–600. Null = không có kỳ cuối theo số kỳ. Không đặt cùng <see cref="EndDate"/> (Phase 92, BR-INS-01).</summary>
    public int? TotalOccurrences { get; set; }

    /// <summary>Không chiếu kỳ nào sau ngày này (giờ VN). Không đặt cùng <see cref="TotalOccurrences"/>.</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>Ngày gốc trong tháng (1–31) của khoản chu kỳ tháng; tháng ngắn hơn thì về ngày cuối tháng (BR-INS-05). Null với chu kỳ khác.</summary>
    public int? AnchorDay { get; set; }

    /// <summary>Số kỳ đã trả trước khi dùng app, user nhập (BR-INS-03). Số kỳ đã trả = liên kết còn sống + số này; không có bộ đếm riêng.</summary>
    public int PrepaidOccurrences { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Hoàn thành do user tất toán trước hạn (BR-INS-08).</summary>
    public bool SettledEarly { get; set; }

    /// <summary>
    /// Số liên kết thanh toán còn sống, repository điền khi đọc (không lưu cột). Dùng <see cref="PaidOccurrences"/> thay vì đọc trực tiếp.
    /// </summary>
    public int LivePaymentCount { get; set; }

    /// <summary>Số kỳ đã trả = liên kết còn sống + <see cref="PrepaidOccurrences"/> (BR-INS-03).</summary>
    public int PaidOccurrences => LivePaymentCount + PrepaidOccurrences;

    public DateTimeOffset? LastSeenAt { get; set; }
    public long? LastSeenAmountCents { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public User? User { get; set; }
    public Category? Category { get; set; }
    public FinancialAccount? FinancialAccount { get; set; }
    public ICollection<RecurringItemPayment> Payments { get; set; } = new List<RecurringItemPayment>();
}
