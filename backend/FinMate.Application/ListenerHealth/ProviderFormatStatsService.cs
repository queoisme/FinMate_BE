using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.ListenerHealth;

/// <param name="ReadableRate24h">% thông báo đọc được (không phải <c>extraction_failed</c>/<c>uncertain</c>) trong 24 giờ; null khi không có mẫu.</param>
/// <param name="Total7d">Số thông báo trong 7 ngày TRƯỚC 24 giờ gần nhất: đúng cửa sổ nền mà job giám sát so sánh.</param>
public sealed record ProviderFormatStatsDto(
    string ProviderKey,
    string DisplayName,
    int Total24h,
    double? ReadableRate24h,
    double? FinancialShare24h,
    int Total7d,
    double? ReadableRate7d,
    double? FinancialShare7d);

/// <param name="UploadGapsByAppVersion">S2: số ca <c>upload_gap</c> trong 7 ngày gần nhất theo phiên bản app (tổng các ảnh chụp hằng ngày).</param>
public sealed record ProviderFormatsDto(IReadOnlyList<ProviderFormatStatsDto> Providers, IReadOnlyDictionary<string, int> UploadGapsByAppVersion);

public interface IProviderFormatStatsService
{
    Task<ProviderFormatsDto> GetAsync(CancellationToken ct = default);
}

/// <summary>BR-LH-11: bảng cho Admin xem từng provider đọc được bao nhiêu phần trăm thông báo. Chỉ số đếm: không nội dung, không user.</summary>
public class ProviderFormatStatsService : IProviderFormatStatsService
{
    private const int UploadGapDays = 7;

    private readonly IProviderFormatRepository _repository;
    private readonly Func<DateTimeOffset> _now;

    public ProviderFormatStatsService(IProviderFormatRepository repository)
        : this(repository, () => DateTimeOffset.UtcNow)
    {
    }

    internal ProviderFormatStatsService(IProviderFormatRepository repository, Func<DateTimeOffset> now)
    {
        _repository = repository;
        _now = now;
    }

    public async Task<ProviderFormatsDto> GetAsync(CancellationToken ct = default)
    {
        var now = _now();
        var counts = await _repository.CountAsync(now, ct);
        var gaps = await _repository.CountUploadGapsAsync(VietnamTime.DateOf(now).AddDays(-(UploadGapDays - 1)), ct);
        return new ProviderFormatsDto(
            counts.Select(c => new ProviderFormatStatsDto(
                c.ProviderKey,
                c.DisplayName,
                c.RecentTotal,
                Rate(c.RecentTotal - c.RecentFailed, c.RecentTotal),
                Rate(c.RecentFinancial, c.RecentTotal),
                c.BaselineTotal,
                Rate(c.BaselineTotal - c.BaselineFailed, c.BaselineTotal),
                Rate(c.BaselineFinancial, c.BaselineTotal))).ToList(),
            gaps);
    }

    private static double? Rate(int part, int total) => total == 0 ? null : Math.Round(100.0 * part / total, 1);
}
