namespace FinMate.Domain.Entities;

public enum AssumptionStatus
{
    Draft,
    Approved,
    Retired,

    /// <summary>Phase 95: bản nháp bị từ chối kèm ghi chú; kết thúc.</summary>
    Rejected,
}

/// <summary>
/// Một giả định kinh tế mà agent được phép trích dẫn (giá xe tăng bao nhiêu %/năm, lãi suất...). Tỉ lệ lưu bằng điểm cơ bản <c>int</c> (1% = 100) — không có <c>decimal</c>/<c>double</c>.
/// Bản ghi KHÔNG bao giờ sửa tại chỗ: đổi giá trị = thêm phiên bản mới, duyệt phiên bản đó thì bản cũ tự về <see cref="AssumptionStatus.Retired"/>.
/// Luôn có nguồn (tên + đường dẫn) và ngày của số liệu để câu trả lời của agent nói được "theo ai, ngày nào".
/// </summary>
public class EconomicAssumption
{
    public Guid Id { get; set; }

    /// <summary>Định danh bất biến, dạng <c>snake_case</c> (ví dụ <c>car_price_growth</c>).</summary>
    public string Key { get; set; } = string.Empty;

    public int RateBps { get; set; }

    public string Unit { get; set; } = "pct_per_year";
    public string SourceName { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>Ngày của SỐ LIỆU (không phải ngày nhập): độ mới của bằng chứng tính từ đây.</summary>
    public DateTimeOffset AsOf { get; set; }

    public int Version { get; set; }
    public AssumptionStatus Status { get; set; } = AssumptionStatus.Draft;
    public Guid CreatedBy { get; set; }
    public Guid? ApprovedBy { get; set; }

    /// <summary>Phase 95: người từ chối (khác người soạn) và ghi chú. Ghi chú không vào audit.</summary>
    public Guid? RejectedBy { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }
    public string? DecisionNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
