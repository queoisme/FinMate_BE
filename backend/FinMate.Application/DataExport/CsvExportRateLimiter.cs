using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.DataExport;

/// <summary>
/// S3: tối đa <c>dataExport.csvPerHour</c> file CSV mỗi user trong 60 phút trượt. Lưu danh sách thời điểm trong cache (Redis), đọc-sửa-ghi không khoá:
/// hai request cùng lúc có thể cùng lọt ở lượt cuối, chấp nhận được với một giới hạn chống lạm dụng. Chỉ lần trả file thành công mới bị đếm.
/// </summary>
public sealed class CsvExportRateLimiter(ICacheService cache, TimeProvider clock)
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    public async Task EnsureAllowedAsync(Guid userId, int perHour, CancellationToken ct = default)
    {
        var recent = await RecentAsync(userId, ct);
        if (recent.Count >= perHour)
        {
            throw new TooManyRequestsException(DataExportErrorCodes.RateLimited,
                "Bạn đã xuất file giao dịch nhiều lần trong một giờ. Vui lòng thử lại sau.", DateTimeOffset.FromUnixTimeMilliseconds(recent.Min()) + Window);
        }
    }

    public async Task RecordAsync(Guid userId, CancellationToken ct = default)
    {
        var recent = await RecentAsync(userId, ct);
        recent.Add(clock.GetUtcNow().ToUnixTimeMilliseconds());
        await cache.SetAsync(Key(userId), recent.ToArray(), Window, ct);
    }

    private async Task<List<long>> RecentAsync(Guid userId, CancellationToken ct)
    {
        var since = (clock.GetUtcNow() - Window).ToUnixTimeMilliseconds();
        return ((await cache.GetAsync<long[]>(Key(userId), ct)) ?? []).Where(t => t > since).ToList();
    }

    private static string Key(Guid userId) => $"export-csv:{userId:N}";
}
