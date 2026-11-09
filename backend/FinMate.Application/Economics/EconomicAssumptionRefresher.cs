using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Agent;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Monitoring;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Economics;

public enum RefreshOutcome { Disabled, AlreadyCurrent, AutoApproved, NeedsReview, Failed }

/// <summary>Lấy một trang web (null khi không tải được). Tách ra để job kiểm thử được không cần mạng.</summary>
public interface IEconomicFeedClient
{
    Task<string?> GetPageAsync(string url, CancellationToken ct = default);
}

/// <summary>Báo kết quả lượt làm mới lên theo dõi hệ thống: khỏe = đã cập nhật; không khỏe = cần người xem.</summary>
public interface IEconomicFeedStatus
{
    Task ReportAsync(bool healthy, string? detail, CancellationToken ct = default);
}

/// <summary>Đăng ký lịch chạy của job làm mới theo cấu hình. Gọi lúc khởi động và sau mỗi lần lưu hoặc hoàn tác cấu hình hệ thống.</summary>
public interface IEconomicFeedScheduler
{
    void Apply(EconomicFeedsSettings settings);
}

public sealed class EconomicFeedStatus(ServiceStatusTracker tracker) : IEconomicFeedStatus
{
    public Task ReportAsync(bool healthy, string? detail, CancellationToken ct = default)
        => tracker.ApplyAsync(
            MonitoredServices.EconomicFeed, IncidentSource.Provider,
            new ProbeResult(healthy ? MonitoredServiceState.Healthy : MonitoredServiceState.Unhealthy, detail),
            failureThreshold: 1, recoveryThreshold: 1, DateTimeOffset.UtcNow, ct);
}

/// <summary>
/// Làm mới giả định lạm phát hằng năm từ thông cáo CPI của Cục Thống kê: trang chỉ mục → bài mới nhất → <see cref="CpiReleaseParser"/>.
/// Chỉ tự duyệt khi số nằm trong khoảng cho phép VÀ lệch ít so với bản đang dùng; còn lại để Draft cho người duyệt.
/// Đọc hỏng thì giữ nguyên bản cũ và báo sự cố; không bao giờ ném lỗi ra ngoài.
/// </summary>
public sealed partial class EconomicAssumptionRefresher(
    ISystemConfigProvider config, IEconomicFeedClient feed, IAssumptionService assumptions, IEconomicFeedStatus status)
{
    public const string Key = "general_price_growth";
    private const string Unit = "pct_per_year";

    // Hệ thống tự làm: dùng chung mã người thực hiện đã có cho việc đồng bộ hệ thống.
    private static readonly Guid SystemActor = Admin.Commands.AiCategorySyncService.SystemActorId;

    [GeneratedRegex("<a\\s[^>]*href=\"(?<href>[^\"]+)\"[^>]*>(?<text>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Anchor();

    public async Task<RefreshOutcome> RunAsync(CancellationToken ct = default)
    {
        var settings = (await config.GetAsync(ct)).Document.EconomicFeeds;
        if (string.IsNullOrWhiteSpace(settings.CpiIndexUrl))
        {
            return RefreshOutcome.Disabled;
        }

        try
        {
            var index = await feed.GetPageAsync(settings.CpiIndexUrl, ct);
            var releaseUrl = index is null ? null : FindLatestReleaseUrl(index, settings.CpiIndexUrl);
            if (releaseUrl is null)
            {
                return await FailAsync("Không tìm thấy bài CPI trên trang chỉ mục.", ct);
            }

            var existing = await assumptions.ListAsync(Key, ct);
            if (existing.Any(a => a.SourceUrl == releaseUrl))
            {
                return RefreshOutcome.AlreadyCurrent;
            }

            var release = CpiReleaseParser.TryParse(await feed.GetPageAsync(releaseUrl, ct));
            if (release is null)
            {
                return await FailAsync("Không đọc được số CPI từ bài mới nhất (trang có thể đã đổi giao diện).", ct);
            }

            var rate = release.AveragePercentBps;
            var sourceName = string.Create(CultureInfo.InvariantCulture,
                $"Cục Thống kê — Chỉ số giá tiêu dùng tháng {release.Month}/{release.Year}: CPI bình quân tăng {rate / 100m:0.##}% so với cùng kỳ năm trước");
            var asOf = new DateTimeOffset(release.PublishedOn.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7));

            var draft = await assumptions.CreateDraftAsync(SystemActor, Key, rate, Unit, sourceName, releaseUrl, asOf, null, ct);

            var current = existing.FirstOrDefault(a => a.Status == AssumptionStatus.Approved);
            var inBounds = rate >= settings.MinRateBps && rate <= settings.MaxRateBps;
            var smallMove = current is null || Math.Abs(rate - current.RateBps) <= settings.AutoApproveMaxDeltaBps;
            if (inBounds && smallMove)
            {
                await assumptions.ApproveAsync(draft.Id, SystemActor, null, ct);
                await status.ReportAsync(true, null, ct);
                return RefreshOutcome.AutoApproved;
            }

            await status.ReportAsync(false, "Số CPI mới lệch nhiều hoặc ngoài khoảng cho phép: đang chờ người duyệt ở trang Giả định kinh tế.", ct);
            return RefreshOutcome.NeedsReview;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Chỉ báo loại lỗi, không báo nội dung: thông báo lỗi có thể chứa địa chỉ hoặc dữ liệu nhạy cảm.
            return await FailAsync($"Lỗi khi làm mới CPI ({ex.GetType().Name}).", ct);
        }
    }

    private async Task<RefreshOutcome> FailAsync(string detail, CancellationToken ct)
    {
        await status.ReportAsync(false, detail, ct);
        return RefreshOutcome.Failed;
    }

    private static string? FindLatestReleaseUrl(string indexHtml, string indexUrl)
    {
        foreach (Match m in Anchor().Matches(indexHtml))
        {
            var text = WebUtility.HtmlDecode(Regex.Replace(m.Groups["text"].Value, "<[^>]+>", " "));
            if (!text.Contains("Chỉ số giá tiêu dùng", StringComparison.OrdinalIgnoreCase)
                || !Uri.TryCreate(new Uri(indexUrl), WebUtility.HtmlDecode(m.Groups["href"].Value), out var url)
                || url.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }

            return url.ToString();
        }

        return null;
    }
}
