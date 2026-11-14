using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FluentValidation;
using FluentValidation.Results;

namespace FinMate.Application.ListenerHealth;

public sealed record AccountCoverageDto(
    Guid AccountId,
    string AccountName,
    string Status,
    DateTimeOffset? LastNotificationAt,
    int? SilentDays,
    int DaysWithNotifications,
    bool NotificationsOff);

public sealed record DeviceCoverageDto(
    Guid InstallationId,
    string Status,
    bool PermissionGranted,
    bool ListenerConnected,
    DateTimeOffset LastReportedAt,
    string? AppVersion,
    string? Manufacturer);

/// <param name="CoveragePercent">Null khi <see cref="Level"/> là <c>unknown</c> (app chưa báo trạng thái lần nào).</param>
public sealed record DataCoverageDto(
    int WindowDays,
    int? CoveragePercent,
    string Level,
    IReadOnlyList<AccountCoverageDto> Accounts,
    IReadOnlyList<DeviceCoverageDto> Devices);

public interface IDataCoverageService
{
    /// <summary>BR-LH-07: độ phủ trong <paramref name="days"/> ngày (7–90), từng ví đang theo dõi và từng thiết bị.</summary>
    Task<DataCoverageDto> GetAsync(Guid userId, int days, CancellationToken ct = default);
}

public class DataCoverageService : IDataCoverageService
{
    public const int DefaultDays = 30;

    private readonly IListenerHealthReader _reader;
    private readonly IListenerHealthRepository _repository;
    private readonly ISystemConfigProvider _config;
    private readonly Func<DateTimeOffset> _now;

    public DataCoverageService(IListenerHealthReader reader, IListenerHealthRepository repository, ISystemConfigProvider config)
        : this(reader, repository, config, () => DateTimeOffset.UtcNow)
    {
    }

    internal DataCoverageService(IListenerHealthReader reader, IListenerHealthRepository repository, ISystemConfigProvider config, Func<DateTimeOffset> now)
    {
        _reader = reader;
        _repository = repository;
        _config = config;
        _now = now;
    }

    public async Task<DataCoverageDto> GetAsync(Guid userId, int days, CancellationToken ct = default)
    {
        if (days is < DataCoverageCache.MinWindowDays or > DataCoverageCache.MaxWindowDays)
        {
            throw new ValidationException([new ValidationFailure(nameof(days), "Số ngày phải từ 7 đến 90.")]);
        }

        var document = (await _config.GetAsync(ct)).Document;
        var now = _now();
        var today = VietnamTime.DateOf(now);
        var snapshot = await _reader.ReadAsync(userId, now, document.ListenerHealth, ct);
        var coverage = await CoverageAsync(snapshot, userId, today, days, document, ct);
        var dayCounts = await _repository.GetNotificationDayCountsAsync(userId, snapshot.Accounts.Select(a => a.Account.Id).ToList(), today.AddDays(-(days - 1)), ct);

        return new DataCoverageDto(
            days,
            coverage.Percent,
            ListenerHealthText.Level(coverage.Level),
            snapshot.Accounts.Select(a => new AccountCoverageDto(
                a.Account.Id,
                a.Account.AccountName,
                ListenerHealthText.Account(a.Verdict.Status),
                a.Verdict.LastNotificationAt,
                a.Verdict.LastNotificationAt is null ? null : a.Verdict.SilentDays,
                dayCounts.TryGetValue(a.Account.Id, out var n) ? n : 0,
                a.NotificationsOff)).ToList(),
            snapshot.Devices.Select(d => new DeviceCoverageDto(
                d.Status.InstallationId,
                ListenerHealthText.Device(d.State),
                d.Status.PermissionGranted,
                d.Status.ListenerConnected,
                d.Status.LastReportedAt,
                d.Status.AppVersion,
                d.Status.Manufacturer)).ToList());
    }

    /// <summary>Độ phủ của một cửa sổ, dùng chung cho endpoint này và cho dự báo (S7).</summary>
    internal async Task<CoverageResult> CoverageAsync(
        ListenerHealthSnapshot snapshot, Guid userId, DateOnly today, int days, SystemConfigDocument document, CancellationToken ct)
    {
        if (snapshot.FirstMonitoredAt is null || snapshot.Devices.Count == 0)
        {
            return CoverageResult.Unknown(days);
        }

        // Lùi thêm vài ngày để ngày đầu cửa sổ còn kế thừa được lần báo trước nó (S4, tối đa 72 giờ).
        var rows = await _repository.GetDaysAsync(userId, today.AddDays(-(days - 1) - 3), ct);
        var byDevice = rows.ToLookup(r => r.StatusId);
        var devices = snapshot.Devices.Select(d => new CoverageDevice(d.Status.Id, d.Status.FirstReportedAt, byDevice[d.Status.Id].ToList())).ToList();
        return CoverageCalculator.Compute(
            today, days, snapshot.FirstMonitoredAt, devices,
            new CoverageThresholds(document.Forecast.GoodCoveragePercent, document.Forecast.MinCoveragePercent, document.ListenerHealth.CoverageInheritHours));
    }
}

/// <summary>Chữ snake_case của các trạng thái trong response.</summary>
public static class ListenerHealthText
{
    public static string Level(CoverageLevel level) => level switch
    {
        CoverageLevel.Good => "good",
        CoverageLevel.Partial => "partial",
        CoverageLevel.Poor => "poor",
        _ => "unknown",
    };

    public static string Account(AccountSilenceStatus status) => status switch
    {
        AccountSilenceStatus.Ok => "ok",
        AccountSilenceStatus.Silent => "silent",
        AccountSilenceStatus.UploadGap => "upload_gap",
        _ => "not_enough_data",
    };

    public static string Device(DeviceHealthState state) => state switch
    {
        DeviceHealthState.Ok => "ok",
        DeviceHealthState.PermissionLost => "permission_lost",
        _ => "unknown",
    };
}
