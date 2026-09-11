namespace FinMate.Application.Common;

/// <summary>
/// Lịch theo giờ Việt Nam (UTC+7 cố định) — dùng cho chu kỳ budget, biên ngày của
/// daily_summaries, streak và chu kỳ mission.
///
/// Dùng offset cố định thay vì <c>TimeZoneInfo.FindSystemTimeZoneById</c>:
/// <c>InvariantGlobalization=true</c> bật solution-wide trong <c>Directory.Build.props</c> nên
/// API phụ thuộc ICU không đáng tin — cùng lý do đã buộc viết lại <c>Slugify</c> ở Phase 3.
/// App chỉ phục vụ thị trường VN, và VN không có DST nên offset cố định là đúng.
///
/// Hai ràng buộc phải nhớ khi dùng:
/// <list type="number">
/// <item>Mọi <c>DateTimeOffset</c> trả về đều ở <b>offset 0</b>. Npgsql từ chối ghi
/// <c>DateTimeOffset</c> có offset khác 0 vào cột <c>timestamptz</c>, kể cả khi giá trị chỉ
/// dùng làm tham số truy vấn.</item>
/// <item>Vì thế <b>không đọc thẳng</b> <c>.Year</c>/<c>.Month</c>/<c>.Day</c> của giá trị trả
/// về — mốc đầu tháng 10 giờ VN nằm ở 30/09 theo UTC. Dùng <see cref="YearMonthOf"/> hoặc
/// <see cref="DateOf"/>.</item>
/// </list>
/// </summary>
public static class VietnamTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(7);

    public static DateTimeOffset ToLocal(DateTimeOffset instant) => instant.ToOffset(Offset);

    /// <summary>Ngày theo lịch VN của một mốc thời gian.</summary>
    public static DateOnly DateOf(DateTimeOffset instant) => DateOnly.FromDateTime(ToLocal(instant).DateTime);

    /// <summary>Hôm nay theo lịch VN.</summary>
    public static DateOnly Today() => DateOf(DateTimeOffset.UtcNow);

    /// <summary>Năm/tháng theo lịch VN — dùng để dựng cache key.</summary>
    public static (int Year, int Month) YearMonthOf(DateTimeOffset instant)
    {
        var local = ToLocal(instant);
        return (local.Year, local.Month);
    }

    /// <summary>Nửa mở [start, end) của tháng chứa <paramref name="instant"/> theo giờ VN.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) MonthRange(DateTimeOffset instant)
    {
        var local = ToLocal(instant);
        return MonthRange(local.Year, local.Month);
    }

    /// <summary>Nửa mở [start, end) của tháng <paramref name="year"/>/<paramref name="month"/> theo giờ VN.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) MonthRange(int year, int month)
    {
        var start = new DateTimeOffset(year, month, 1, 0, 0, 0, Offset);
        return (start.ToUniversalTime(), start.AddMonths(1).ToUniversalTime());
    }

    /// <summary>Nửa mở [start, end) của một ngày theo giờ VN.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) DayRange(DateOnly date)
    {
        var start = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, Offset);
        return (start.ToUniversalTime(), start.AddDays(1).ToUniversalTime());
    }

    /// <summary>Nửa mở [start, end) phủ trọn các ngày từ <paramref name="from"/> tới <paramref name="to"/> (bao gồm cả hai).</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) DayRange(DateOnly from, DateOnly to)
        => (DayRange(from).Start, DayRange(to).End);

    public static DateTimeOffset CurrentMonthStart() => MonthRange(DateTimeOffset.UtcNow).Start;
}
