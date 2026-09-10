namespace FinMate.Application.Budgets;

/// <summary>
/// Biên chu kỳ budget. Tính theo giờ Việt Nam (UTC+7) cố định thay vì
/// <c>TimeZoneInfo.FindSystemTimeZoneById</c>: <c>InvariantGlobalization=true</c> bật
/// solution-wide trong <c>Directory.Build.props</c> nên API phụ thuộc ICU không đáng tin —
/// cùng lý do đã buộc viết lại <c>Slugify</c> ở Phase 3. App chỉ phục vụ thị trường VN.
/// </summary>
public static class BudgetCalendar
{
    public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>Nửa mở [start, end) của tháng chứa <paramref name="instant"/> theo giờ VN.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) MonthlyPeriod(DateTimeOffset instant)
    {
        var local = instant.ToOffset(VietnamOffset);
        return MonthlyPeriod(local.Year, local.Month);
    }

    /// <summary>Nửa mở [start, end) của tháng <paramref name="year"/>/<paramref name="month"/> theo giờ VN.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) MonthlyPeriod(int year, int month)
    {
        var start = new DateTimeOffset(year, month, 1, 0, 0, 0, VietnamOffset);

        // Trả về ở UTC (cùng mốc thời gian, offset 0): Npgsql chỉ ghi được DateTimeOffset có
        // offset 0 vào cột `timestamp with time zone`, kể cả khi dùng làm tham số truy vấn.
        // Ranh giới tháng vẫn là ranh giới theo giờ VN — chỉ cách biểu diễn đổi.
        return (start.ToUniversalTime(), start.AddMonths(1).ToUniversalTime());
    }

    public static DateTimeOffset CurrentMonthStart() => MonthlyPeriod(DateTimeOffset.UtcNow).Start;

    /// <summary>Năm/tháng theo giờ VN của một mốc chu kỳ — dùng để dựng cache key.</summary>
    public static (int Year, int Month) VietnamYearMonth(DateTimeOffset instant)
    {
        var local = instant.ToOffset(VietnamOffset);
        return (local.Year, local.Month);
    }
}
