using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;

namespace FinMate.Application.ListenerHealth;

/// <param name="UploadGapAppVersions">Phiên bản app của mỗi ví đang <c>upload_gap</c> (S2), để job đếm theo phiên bản; không gắn với user.</param>
public sealed record ListenerHealthRunResult(int RemindersRaised, IReadOnlyList<string?> UploadGapAppVersions);

public interface IListenerHealthReminderService
{
    /// <summary>BR-LH-05: nhắc một user nếu thiết bị mất quyền hoặc ví im lặng bất thường. An toàn khi chạy lại nhiều lần trong ngày.</summary>
    Task<ListenerHealthRunResult> RunForUserAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default);
}

/// <summary>
/// Nhắc qua hộp thư <c>forecast_alerts</c> (kind <c>listener_health</c>). Danh tính của một đợt nằm trong <c>dedup_key</c> (D17):
/// <c>listener_health:{device|account|upload}:{id}:{mốc}:{n}</c>, với mốc là lúc mất quyền hoặc thông báo cuối ở server. Có thông báo mới hay quyền được cấp lại
/// thì mốc đổi, tự sang đợt mới. Mỗi đợt tối đa <c>maxRemindersPerEpisode</c> lần, cách nhau ít nhất <c>cooldownDays</c> ngày lịch giờ VN.
/// Không có số tiền; tên ví do user đặt nằm trong payload (payload không bao giờ được log).
/// </summary>
public class ListenerHealthReminderService : IListenerHealthReminderService
{
    public const string ReasonPermissionLost = "permission_lost";
    public const string ReasonAccountSilent = "account_silent";
    public const string HintBankNotificationsOff = "bank_notifications_off";
    public const string HintSyncPending = "sync_pending";
    private const int TitleNameMaxLength = 40;

    private readonly IListenerHealthReader _reader;
    private readonly IForecastAlertRepository _alerts;
    private readonly IForecastAlertDispatcher _dispatcher;
    private readonly ISystemConfigProvider _config;

    public ListenerHealthReminderService(
        IListenerHealthReader reader, IForecastAlertRepository alerts, IForecastAlertDispatcher dispatcher, ISystemConfigProvider config)
    {
        _reader = reader;
        _alerts = alerts;
        _dispatcher = dispatcher;
        _config = config;
    }

    public async Task<ListenerHealthRunResult> RunForUserAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        var settings = (await _config.GetAsync(ct)).Document.ListenerHealth;
        var snapshot = await _reader.ReadAsync(userId, now, settings, ct);
        if (snapshot.Accounts.Count == 0)
        {
            return new ListenerHealthRunResult(0, []);   // BR-LH-04/06: không có ví đang theo dõi thì không có gì để nhắc
        }

        var raised = 0;
        var lost = snapshot.Devices.Where(d => d.State == DeviceHealthState.PermissionLost && d.Status.PermissionLostSince is not null).ToList();
        foreach (var device in lost)
        {
            var prefix = $"listener_health:device:{device.Status.Id:N}:{device.Status.PermissionLostSince!.Value.ToUnixTimeSeconds()}:";
            raised += await TryRemindAsync(userId, prefix, now, settings, n => new ForecastAlertRequest(
                userId,
                ForecastAlertKind.ListenerHealth,
                prefix + n,
                "FinMate không còn đọc được thông báo ngân hàng",
                "Quyền đọc thông báo trên máy đã bị tắt nên giao dịch mới sẽ không được ghi. Mở app để bật lại nhé.",
                new Dictionary<string, string>
                {
                    ["reason"] = ReasonPermissionLost,
                    ["installationId"] = device.Status.InstallationId.ToString(),
                    ["manufacturer"] = device.Status.Manufacturer ?? string.Empty,
                },
                HonorQuietHours: true), ct);
        }

        // D9: thiết bị mất quyền giải thích sự im lặng của các ví nó theo dõi; thiết bị không báo danh sách app thì giải thích mọi ví.
        var explained = lost.SelectMany(d => d.Packages.Select(p => p.PackageName)).ToHashSet(StringComparer.Ordinal);
        var explainsAll = lost.Any(d => d.Packages.Count == 0);

        foreach (var account in snapshot.Accounts)
        {
            var status = account.Verdict.Status;
            if (status is not (AccountSilenceStatus.Silent or AccountSilenceStatus.UploadGap)
                || explainsAll || explained.Contains(account.Account.PackageName!) || account.Verdict.LastNotificationAt is not { } last)
            {
                continue;
            }

            var upload = status == AccountSilenceStatus.UploadGap;
            var prefix = $"listener_health:{(upload ? "upload" : "account")}:{account.Account.Id:N}:{last.ToUnixTimeSeconds()}:";
            var name = account.Account.AccountName;
            var hint = upload ? HintSyncPending : account.NotificationsOff ? HintBankNotificationsOff : null;
            var days = account.Verdict.SilentDays;
            var body = hint switch
            {
                HintSyncPending => $"Đã {days} ngày FinMate chưa nhận được giao dịch từ {name} dù máy vẫn có thông báo. Mở app để đồng bộ nhé.",
                HintBankNotificationsOff => $"Đã {days} ngày FinMate không nhận được thông báo từ {name}. Thông báo của {name} đang bị tắt trên máy.",
                _ => $"Đã {days} ngày FinMate không nhận được thông báo từ {name}. Có thể app bị tắt chạy nền hoặc thông báo ngân hàng đang tắt.",
            };
            var data = new Dictionary<string, string>
            {
                ["reason"] = ReasonAccountSilent,
                ["accountId"] = account.Account.Id.ToString(),
                ["accountName"] = name,
                ["silentDays"] = days.ToString(),
            };
            if (hint is not null)
            {
                data["hint"] = hint;
            }

            raised += await TryRemindAsync(userId, prefix, now, settings, n => new ForecastAlertRequest(
                userId,
                ForecastAlertKind.ListenerHealth,
                prefix + n,
                $"Chưa thấy giao dịch nào từ {Shorten(name)}",
                body,
                data,
                HonorQuietHours: true), ct);
        }

        var gaps = snapshot.Accounts.Where(a => a.Verdict.Status == AccountSilenceStatus.UploadGap).Select(a => a.UploadGapAppVersion).ToList();
        return new ListenerHealthRunResult(raised, gaps);
    }

    private async Task<int> TryRemindAsync(
        Guid userId, string prefix, DateTimeOffset now, ListenerHealthSettings settings, Func<int, ForecastAlertRequest> request, CancellationToken ct)
    {
        var previous = await _alerts.ListCreatedWithKeyPrefixAsync(userId, ForecastAlertKind.ListenerHealth, prefix, ct);
        if (previous.Count >= settings.MaxRemindersPerEpisode)
        {
            return 0;
        }

        if (previous.Count > 0 && VietnamTime.DateOf(now).DayNumber - VietnamTime.DateOf(previous[^1]).DayNumber < settings.CooldownDays)
        {
            return 0;
        }

        return await _dispatcher.RaiseAsync(request(previous.Count + 1), ct) is null ? 0 : 1;
    }

    private static string Shorten(string name) => name.Length <= TitleNameMaxLength ? name : name[..(TitleNameMaxLength - 1)] + "…";
}
