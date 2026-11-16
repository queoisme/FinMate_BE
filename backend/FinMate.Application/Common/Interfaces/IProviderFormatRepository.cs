namespace FinMate.Application.Common.Interfaces;

/// <summary>Số đếm của một provider: 24 giờ gần nhất và 7 ngày trước đó. Không tính kết quả <c>error</c> (AI service hỏng, D13). Chỉ số, không nội dung, không user.</summary>
public sealed record ProviderFormatCounts(
    string ProviderKey,
    string DisplayName,
    int RecentTotal,
    int RecentFailed,
    int RecentFinancial,
    int BaselineTotal,
    int BaselineFailed,
    int BaselineFinancial);

public interface IProviderFormatRepository
{
    /// <summary>
    /// Đếm <c>ai_results</c> theo provider (khớp <c>notification_logs.package_name</c> với <c>provider_configs.package_name</c>): cửa sổ gần
    /// <c>[now − 24h, now)</c> và nền <c>[now − 8 ngày, now − 24h)</c>. "Failed" = <c>extraction_failed</c> + <c>uncertain</c>.
    /// </summary>
    Task<IReadOnlyList<ProviderFormatCounts>> CountAsync(DateTimeOffset now, CancellationToken ct = default);

    /// <summary>S2: tổng số ca <c>upload_gap</c> theo phiên bản app từ <paramref name="fromDay"/> (gồm cả ngày đó).</summary>
    Task<IReadOnlyDictionary<string, int>> CountUploadGapsAsync(DateOnly fromDay, CancellationToken ct = default);
}
