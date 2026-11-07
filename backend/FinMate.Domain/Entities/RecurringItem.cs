namespace FinMate.Domain.Entities;

public enum RecurringKind
{
    /// <summary>Khoản phải trả định kỳ (tiền nhà, hóa đơn, gói thuê bao...).</summary>
    Obligation,

    /// <summary>Khoản thu định kỳ (lương, trợ cấp...).</summary>
    Inflow,
}

/// <summary>
/// Một khoản thu/chi định kỳ MÀ NGƯỜI DÙNG ĐÃ XÁC NHẬN (hệ thống chỉ đề xuất ứng viên từ lịch sử giao dịch; không tự lưu). Là đầu vào của sổ dòng tiền theo ngày khi mô phỏng một khoản mua:
/// khoản đã xác nhận được tính là nguồn <c>Confirmed</c>, còn ứng viên chưa xác nhận chỉ là <c>Estimated</c>. Số tiền là đồng VND trong <c>BIGINT</c> (tên cột <c>amount_cents</c> theo quy ước chung của dự án, không chia 100).
/// </summary>
public class RecurringItem
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public RecurringKind Kind { get; set; }

    /// <summary>Ngày trong tháng (1–31). Tháng ngắn hơn thì rơi vào ngày cuối tháng.</summary>
    public int DayOfMonth { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }
}
