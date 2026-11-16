using FinMate.Application.Admin.SystemConfig;

namespace FinMate.Application.ListenerHealth;

public enum AccountSilenceStatus
{
    Ok,

    /// <summary>Im lặng lâu hơn mức cho phép và không thiết bị nào còn thấy app này: nghi listener đã ngừng.</summary>
    Silent,

    /// <summary>Ít hơn ngưỡng thông báo trong cửa sổ: ví ít dùng, im lặng là bình thường, không xét.</summary>
    NotEnoughData,

    /// <summary>Server im quá mức nhưng listener vẫn thấy thông báo của app (S2): lỗi nằm ở đường đồng bộ, không phải ở quyền.</summary>
    UploadGap,
}

/// <param name="LastNotificationAt">Thông báo cuối server nhận được của ví (đã kẹp về hiện tại). Cũng là mốc của một đợt im lặng (D17).</param>
/// <param name="AllowedHours">Khoảng im lặng cho phép; null khi không đủ dữ liệu.</param>
/// <param name="SilentDays">Số ngày trọn vẹn kể từ thông báo cuối ở server.</param>
public sealed record SilenceVerdict(AccountSilenceStatus Status, DateTimeOffset? LastNotificationAt, double? AllowedHours, int SilentDays);

/// <summary>
/// BR-LH-03: một ví đang theo dõi có im lặng bất thường không. Hàm thuần, chỉ dựa vào thời điểm thông báo ở server và lần cuối listener thấy app đó.
/// <list type="bullet">
/// <item>Cửa sổ lịch sử kết thúc ở thông báo cuối (không phải ở hiện tại), để một đợt im lặng dài vẫn là <c>silent</c> chứ không tụt thành "không đủ dữ liệu".</item>
/// <item>P90 theo nearest-rank trên khoảng cách giữa hai thông báo liên tiếp trong cửa sổ; khoảng từ thông báo cuối tới hiện tại không thuộc phân bố (D19).</item>
/// <item>Thời điểm ở tương lai (đồng hồ máy lệch) bị kẹp về hiện tại.</item>
/// </list>
/// </summary>
public static class SilenceDetector
{
    private const double Percentile = 0.9;

    public static SilenceVerdict Evaluate(
        IReadOnlyCollection<DateTimeOffset> receivedAt, DateTimeOffset? lastPostedAt, DateTimeOffset now, ListenerHealthSettings settings)
    {
        if (receivedAt.Count == 0)
        {
            return new SilenceVerdict(AccountSilenceStatus.NotEnoughData, null, null, 0);
        }

        var times = receivedAt.Select(t => t > now ? now : t).OrderBy(t => t).ToList();
        var last = times[^1];
        var windowStart = last.AddDays(-settings.HistoryDays);
        var window = times.Where(t => t >= windowStart).ToList();
        var silentHours = (now - last).TotalHours;
        var silentDays = (int)Math.Floor(silentHours / 24);

        if (window.Count < settings.MinNotificationsForBaseline)
        {
            return new SilenceVerdict(AccountSilenceStatus.NotEnoughData, last, null, silentDays);
        }

        var allowed = Math.Max(settings.MinSilenceHours, settings.SilenceMultiplier * P90(window));
        if (silentHours <= allowed)
        {
            return new SilenceVerdict(AccountSilenceStatus.Ok, last, allowed, silentDays);
        }

        var seen = lastPostedAt is { } posted ? (posted > now ? now : posted) : (DateTimeOffset?)null;
        var status = seen is { } s && (now - s).TotalHours <= allowed ? AccountSilenceStatus.UploadGap : AccountSilenceStatus.Silent;
        return new SilenceVerdict(status, last, allowed, silentDays);
    }

    private static double P90(List<DateTimeOffset> sortedTimes)
    {
        var gaps = sortedTimes.Zip(sortedTimes.Skip(1), (a, b) => (b - a).TotalHours).OrderBy(g => g).ToList();
        var rank = (int)Math.Ceiling(Percentile * gaps.Count) - 1;
        return gaps[Math.Clamp(rank, 0, gaps.Count - 1)];
    }
}
