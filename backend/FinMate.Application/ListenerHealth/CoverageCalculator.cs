using FinMate.Application.Common;
using FinMate.Domain.Entities;

namespace FinMate.Application.ListenerHealth;

public enum CoverageLevel
{
    Good,
    Partial,
    Poor,

    /// <summary>App chưa báo trạng thái lần nào (hoặc chưa có ví theo dõi): không biết, KHÔNG phải kém.</summary>
    Unknown,
}

/// <param name="InheritHours">S4: ngày không có lần báo kế thừa lần báo gần nhất nếu lần đó cách trưa ngày ấy không quá chừng này giờ.</param>
public sealed record CoverageThresholds(int GoodPercent, int MinPercent, int InheritHours);

/// <param name="FirstReportedAt">Lần báo đầu tiên của thiết bị; ngày trước đó thiết bị không được tính là ok.</param>
/// <param name="Days">Các dòng theo ngày của thiết bị (thứ tự bất kỳ).</param>
public sealed record CoverageDevice(Guid Id, DateTimeOffset FirstReportedAt, IReadOnlyList<DeviceListenerDay> Days);

/// <param name="Percent">Null khi <see cref="Level"/> là <c>Unknown</c>.</param>
/// <param name="CountedDays">Số ngày thực sự được tính sau khi bỏ những ngày trước ví đầu tiên và trước lần báo đầu tiên.</param>
public sealed record CoverageResult(int? Percent, CoverageLevel Level, int WindowDays, int CountedDays, int OkDays)
{
    public static CoverageResult Unknown(int windowDays) => new(null, CoverageLevel.Unknown, windowDays, 0, 0);
}

/// <summary>
/// BR-LH-07: phần trăm số ngày (giờ VN) trong cửa sổ có ít nhất một thiết bị <c>permission_ok</c>. Hàm thuần.
/// <list type="bullet">
/// <item>Ngày có dòng: ok theo <see cref="DeviceListenerDay.PermissionOk"/> (S3).</item>
/// <item>Ngày không có dòng: kế thừa trạng thái lần báo cuối của dòng gần nhất trước đó nếu lần báo đó cách 12:00 trưa ngày ấy ≤ <c>InheritHours</c> (S4).
/// Lấy mốc trưa để với 36 giờ, đúng một ngày lỡ báo được bù dù lần báo trước rơi vào sáng sớm hay đêm khuya.</item>
/// <item>Mốc đầu = max(đầu cửa sổ, ngày tạo ví theo dõi đầu tiên, ngày báo đầu tiên) (D7). Không còn ngày nào thì <c>Unknown</c>.</item>
/// <item>Phần trăm làm tròn xuống, để 84,8% không thành "tốt".</item>
/// </list>
/// </summary>
public static class CoverageCalculator
{
    private static readonly TimeOnly Noon = new(12, 0);

    public static CoverageResult Compute(
        DateOnly today, int windowDays, DateTimeOffset? firstMonitoredAt, IReadOnlyCollection<CoverageDevice> devices, CoverageThresholds thresholds)
    {
        if (firstMonitoredAt is null || devices.Count == 0)
        {
            return CoverageResult.Unknown(windowDays);
        }

        var start = Max(today.AddDays(-(windowDays - 1)), VietnamTime.DateOf(firstMonitoredAt.Value), VietnamTime.DateOf(devices.Min(d => d.FirstReportedAt)));
        if (start > today)
        {
            return CoverageResult.Unknown(windowDays);
        }

        var byDevice = devices.Select(d => (Device: d, Days: d.Days.OrderBy(x => x.Day).ToList())).ToList();
        var counted = 0;
        var ok = 0;
        for (var day = start; day <= today; day = day.AddDays(1))
        {
            counted++;
            if (byDevice.Any(d => IsOk(d.Device, d.Days, day, thresholds.InheritHours)))
            {
                ok++;
            }
        }

        var percent = ok * 100 / counted;
        return new CoverageResult(percent, LevelOf(percent, thresholds.GoodPercent, thresholds.MinPercent), windowDays, counted, ok);
    }

    public static CoverageLevel LevelOf(int? percent, int goodPercent, int minPercent)
        => percent is not { } p ? CoverageLevel.Unknown
            : p >= goodPercent ? CoverageLevel.Good
            : p >= minPercent ? CoverageLevel.Partial
            : CoverageLevel.Poor;

    private static bool IsOk(CoverageDevice device, List<DeviceListenerDay> days, DateOnly day, int inheritHours)
    {
        if (day < VietnamTime.DateOf(device.FirstReportedAt))
        {
            return false;
        }

        DeviceListenerDay? previous = null;
        foreach (var row in days)
        {
            if (row.Day == day)
            {
                return row.PermissionOk;
            }

            if (row.Day > day)
            {
                break;
            }

            previous = row;
        }

        if (previous is null)
        {
            return false;
        }

        var noon = new DateTimeOffset(day.ToDateTime(Noon), VietnamTime.Offset);
        return previous.LastStateOk && (noon - previous.LastReportedAt).TotalHours <= inheritHours;
    }

    private static DateOnly Max(DateOnly a, DateOnly b, DateOnly c) => new[] { a, b, c }.Max();
}
