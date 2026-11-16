namespace FinMate.Domain.Entities;

/// <summary>
/// Bản chụp một dự báo số dư để sau này đo độ chính xác: mỗi đêm, mỗi người dùng có dữ liệu, mỗi mốc nhìn trước (7, 14, 30 ngày) một dòng.
/// Đến ngày của mốc thì job điền <see cref="ActualCents"/> bằng số dư thực tế. Số tiền là đồng (VND), tên cột <c>_cents</c> theo quy ước dự án.
/// </summary>
public class ForecastSnapshot
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Ngày (giờ VN) dự báo được tính.</summary>
    public DateOnly OriginDate { get; set; }

    public int HorizonDays { get; set; }

    public DateOnly HorizonDate { get; set; }

    /// <summary>Phương pháp đã tạo con số (ví dụ <c>median_p25p75</c>). Đổi phương pháp thì so sánh được trước và sau.</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>Phiên bản cấu hình hệ thống lúc tính, để truy vết tham số nào đã tạo ra con số này.</summary>
    public int ConfigVersion { get; set; }

    public long StartBalanceCents { get; set; }

    /// <summary>Số dư dự kiến cuối ngày <see cref="HorizonDate"/>.</summary>
    public long PredictedCents { get; set; }

    /// <summary>Đầu thận trọng của dải (chi linh hoạt ở phân vị cao).</summary>
    public long ConservativeCents { get; set; }

    /// <summary>Đầu lạc quan của dải.</summary>
    public long OptimisticCents { get; set; }

    /// <summary>Mốc so sánh B0: chỉ cộng trừ các khoản định kỳ, chi linh hoạt bằng 0 (cách của Simplifi và Monzo).</summary>
    public long RecurringOnlyCents { get; set; }

    public string Confidence { get; set; } = string.Empty;

    /// <summary>Số dư thực tế cuối ngày <see cref="HorizonDate"/>; null cho tới khi tới ngày đó.</summary>
    public long? ActualCents { get; set; }

    public DateTimeOffset? ActualFilledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }
}
