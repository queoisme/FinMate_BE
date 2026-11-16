using System.Text.Json;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;

namespace FinMate.Application.ListenerHealth;

/// <param name="NotificationsOff">Có thiết bị báo thông báo của app này đang bị tắt (D6).</param>
/// <param name="UploadGapAppVersion">Khi <c>upload_gap</c>: phiên bản app của thiết bị thấy app này gần nhất (S2), để đếm theo phiên bản.</param>
public sealed record MonitoredAccountHealth(FinancialAccount Account, SilenceVerdict Verdict, bool NotificationsOff, string? UploadGapAppVersion);

public sealed record DeviceHealth(DeviceListenerStatus Status, DeviceHealthState State, IReadOnlyList<StoredPackage> Packages);

/// <param name="FirstMonitoredAt">Lúc tạo ví theo dõi đầu tiên (D7); null khi không có ví nào đang theo dõi.</param>
public sealed record ListenerHealthSnapshot(IReadOnlyList<MonitoredAccountHealth> Accounts, IReadOnlyList<DeviceHealth> Devices, DateTimeOffset? FirstMonitoredAt);

public interface IListenerHealthReader
{
    Task<ListenerHealthSnapshot> ReadAsync(Guid userId, DateTimeOffset now, ListenerHealthSettings settings, CancellationToken ct = default);
}

/// <summary>
/// Đọc một lần mọi thứ về sức khoẻ listener của một user: ví đang theo dõi (có package, chưa xoá, đang bật theo dõi: BR-LH-06) kèm kết luận im lặng,
/// và thiết bị kèm trạng thái. Dùng chung cho <c>/me/data-coverage</c> và job nhắc, để hai nơi không bao giờ kết luận khác nhau.
/// </summary>
public class ListenerHealthReader : IListenerHealthReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IListenerHealthRepository _repository;
    private readonly IFinancialAccountRepository _accounts;

    public ListenerHealthReader(IListenerHealthRepository repository, IFinancialAccountRepository accounts)
    {
        _repository = repository;
        _accounts = accounts;
    }

    public static bool IsWatched(FinancialAccount account) => account.IsMonitored && account.DeletedAt is null && !string.IsNullOrEmpty(account.PackageName);

    public async Task<ListenerHealthSnapshot> ReadAsync(Guid userId, DateTimeOffset now, ListenerHealthSettings settings, CancellationToken ct = default)
    {
        var watched = (await _accounts.GetListByUserAsync(userId, ct)).Where(IsWatched).ToList();
        var devices = (await _repository.GetStatusesAsync(userId, ct))
            .Select(s => new DeviceHealth(s, DeviceHealthEvaluator.Evaluate(s, now, settings), Packages(s)))
            .ToList();
        var times = await _repository.GetNotificationTimesAsync(userId, watched.Select(a => a.Id).ToList(), settings.HistoryDays, now, ct);

        var accounts = watched.Select(account =>
        {
            var sightings = devices
                .SelectMany(d => d.Packages.Where(p => p.PackageName == account.PackageName).Select(p => (Device: d, Package: p)))
                .ToList();
            var latest = sightings.Where(x => x.Package.LastPostedAt is not null).OrderByDescending(x => x.Package.LastPostedAt).FirstOrDefault();
            var lastPostedAt = latest.Package?.LastPostedAt;
            var verdict = SilenceDetector.Evaluate(times.TryGetValue(account.Id, out var t) ? t : [], lastPostedAt, now, settings);
            return new MonitoredAccountHealth(
                account,
                verdict,
                sightings.Any(x => x.Package.NotificationsEnabled == false),
                verdict.Status == AccountSilenceStatus.UploadGap ? latest.Device.Status.AppVersion : null);
        }).ToList();

        return new ListenerHealthSnapshot(accounts, devices, watched.Count == 0 ? null : watched.Min(a => a.CreatedAt));
    }

    private static IReadOnlyList<StoredPackage> Packages(DeviceListenerStatus status)
    {
        try
        {
            return JsonSerializer.Deserialize<List<StoredPackage>>(status.SelectedPackages, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
