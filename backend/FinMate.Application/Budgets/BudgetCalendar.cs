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
        var start = new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, VietnamOffset);
        return (start, start.AddMonths(1));
    }

    /// <summary>Nửa mở [start, end) của tháng <paramref name="year"/>/<paramref name="month"/> theo giờ VN.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) MonthlyPeriod(int year, int month)
    {
        var start = new DateTimeOffset(year, month, 1, 0, 0, 0, VietnamOffset);
        return (start, start.AddMonths(1));
    }

    public static DateTimeOffset CurrentMonthStart() => MonthlyPeriod(DateTimeOffset.UtcNow).Start;
}
