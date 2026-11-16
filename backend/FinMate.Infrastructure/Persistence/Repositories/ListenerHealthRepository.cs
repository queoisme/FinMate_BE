using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class ListenerHealthRepository : IListenerHealthRepository
{
    private readonly FinMateDbContext _context;

    public ListenerHealthRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<bool> DeviceExistsAsync(Guid userId, Guid installationId, CancellationToken ct = default)
        => _context.DeviceListenerStatuses.AnyAsync(s => s.UserId == userId && s.InstallationId == installationId, ct);

    public Task<int> CountDevicesAsync(Guid userId, CancellationToken ct = default)
        => _context.DeviceListenerStatuses.CountAsync(s => s.UserId == userId, ct);

    public async Task<IReadOnlySet<string>> GetKnownPackagesAsync(Guid userId, CancellationToken ct = default)
    {
        var accounts = await _context.FinancialAccounts
            .Where(a => a.UserId == userId && a.PackageName != null)
            .Select(a => a.PackageName!)
            .ToListAsync(ct);
        var providers = await _context.ProviderConfigs.Select(p => p.PackageName).ToListAsync(ct);
        return accounts.Concat(providers).ToHashSet(StringComparer.Ordinal);
    }

    public Task RecordReportAsync(ListenerReport report, CancellationToken ct = default)
    {
        var r = report with { ReceivedAt = report.ReceivedAt.ToUniversalTime() };
        return _context.Database.ExecuteSqlInterpolatedAsync($"""
            WITH s AS (
                INSERT INTO device_listener_statuses
                    (id, user_id, installation_id, permission_granted, listener_connected, selected_packages, app_version, manufacturer,
                     permission_lost_since, first_reported_at, last_reported_at)
                VALUES
                    (gen_random_uuid(), {r.UserId}, {r.InstallationId}, {r.PermissionGranted}, {r.ListenerConnected}, CAST({r.SelectedPackagesJson} AS jsonb),
                     {r.AppVersion}, {r.Manufacturer}, CASE WHEN {r.PermissionGranted} THEN NULL ELSE {r.ReceivedAt} END, {r.ReceivedAt}, {r.ReceivedAt})
                ON CONFLICT (user_id, installation_id) DO UPDATE SET
                    permission_granted = EXCLUDED.permission_granted,
                    listener_connected = EXCLUDED.listener_connected,
                    selected_packages = EXCLUDED.selected_packages,
                    app_version = EXCLUDED.app_version,
                    manufacturer = EXCLUDED.manufacturer,
                    permission_lost_since = CASE WHEN EXCLUDED.permission_granted THEN NULL
                                                 ELSE COALESCE(device_listener_statuses.permission_lost_since, EXCLUDED.last_reported_at) END,
                    last_reported_at = EXCLUDED.last_reported_at
                RETURNING id
            )
            INSERT INTO device_listener_daily
                (status_id, day, user_id, any_permission_denied, last_permission_granted, last_listener_connected, last_reported_at, report_count)
            SELECT s.id, {r.Day}, {r.UserId}, NOT {r.PermissionGranted}, {r.PermissionGranted}, {r.ListenerConnected}, {r.ReceivedAt}, 1 FROM s
            ON CONFLICT (status_id, day) DO UPDATE SET
                any_permission_denied = device_listener_daily.any_permission_denied OR EXCLUDED.any_permission_denied,
                last_permission_granted = CASE WHEN EXCLUDED.last_reported_at >= device_listener_daily.last_reported_at
                                               THEN EXCLUDED.last_permission_granted ELSE device_listener_daily.last_permission_granted END,
                last_listener_connected = CASE WHEN EXCLUDED.last_reported_at >= device_listener_daily.last_reported_at
                                               THEN EXCLUDED.last_listener_connected ELSE device_listener_daily.last_listener_connected END,
                last_reported_at = GREATEST(device_listener_daily.last_reported_at, EXCLUDED.last_reported_at),
                report_count = device_listener_daily.report_count + 1
            """, ct);
    }

    public async Task<IReadOnlyList<DeviceListenerStatus>> GetStatusesAsync(Guid userId, CancellationToken ct = default)
        => await _context.DeviceListenerStatuses.AsNoTracking().Where(s => s.UserId == userId).OrderBy(s => s.FirstReportedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<DeviceListenerDay>> GetDaysAsync(Guid userId, DateOnly fromDay, CancellationToken ct = default)
        => await _context.DeviceListenerDays.AsNoTracking().Where(d => d.UserId == userId && d.Day >= fromDay).ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, List<DateTimeOffset>>> GetNotificationTimesAsync(
        Guid userId, IReadOnlyCollection<Guid> accountIds, int historyDays, DateTimeOffset now, CancellationToken ct = default)
    {
        if (accountIds.Count == 0)
        {
            return new Dictionary<Guid, List<DateTimeOffset>>();
        }

        var ids = accountIds.ToArray();
        now = now.ToUniversalTime();
        var rows = await _context.Database.SqlQuery<NotificationTimeRow>($"""
            SELECT x.account_id AS "AccountId", x.at AS "At"
            FROM (
                SELECT financial_account_id AS account_id, LEAST(received_at, {now}) AS at,
                       max(LEAST(received_at, {now})) OVER (PARTITION BY financial_account_id) AS last_at
                FROM notification_logs
                WHERE user_id = {userId} AND financial_account_id = ANY({ids})
            ) x
            WHERE x.at >= x.last_at - make_interval(days => {historyDays})
            """).ToListAsync(ct);
        return rows.GroupBy(r => r.AccountId).ToDictionary(g => g.Key, g => g.Select(r => r.At).ToList());
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetNotificationDayCountsAsync(
        Guid userId, IReadOnlyCollection<Guid> accountIds, DateOnly fromDay, CancellationToken ct = default)
    {
        if (accountIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var ids = accountIds.ToArray();
        var from = new DateTimeOffset(fromDay.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime();
        var rows = await _context.Database.SqlQuery<NotificationDayCountRow>($"""
            SELECT financial_account_id AS "AccountId", count(DISTINCT (received_at AT TIME ZONE INTERVAL '+07:00')::date)::int AS "Days"
            FROM notification_logs
            WHERE user_id = {userId} AND financial_account_id = ANY({ids}) AND received_at >= {from}
            GROUP BY financial_account_id
            """).ToListAsync(ct);
        return rows.ToDictionary(r => r.AccountId, r => r.Days);
    }

    public async Task<IReadOnlyList<Guid>> ListWatchedUserIdsAsync(Guid? afterUserId, int limit, CancellationToken ct = default)
    {
        var query = _context.FinancialAccounts
            .Where(a => a.IsMonitored && a.PackageName != null)
            .Select(a => a.UserId)
            .Distinct();
        if (afterUserId is { } after)
        {
            query = query.Where(id => id.CompareTo(after) > 0);
        }

        return await query.OrderBy(id => id).Take(limit).ToListAsync(ct);
    }

    public async Task ReplaceUploadGapCountsAsync(DateOnly day, IReadOnlyDictionary<string, int> countsByAppVersion, CancellationToken ct = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        await _context.ListenerUploadGapDaily.Where(g => g.Day == day).ExecuteDeleteAsync(ct);
        foreach (var (version, count) in countsByAppVersion)
        {
            _context.ListenerUploadGapDaily.Add(new ListenerUploadGapDaily { Day = day, AppVersion = version, Count = count });
        }

        await _context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<int> PurgeDailyRowsBeforeAsync(DateOnly cutoff, int batchSize, CancellationToken ct = default)
    {
        var devices = await _context.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM device_listener_daily WHERE ctid IN (SELECT ctid FROM device_listener_daily WHERE day < {cutoff} LIMIT {batchSize})
            """, ct);
        var gaps = await _context.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM listener_upload_gap_daily WHERE ctid IN (SELECT ctid FROM listener_upload_gap_daily WHERE day < {cutoff} LIMIT {batchSize})
            """, ct);
        return devices + gaps;
    }

    private sealed class NotificationTimeRow
    {
        public Guid AccountId { get; set; }
        public DateTimeOffset At { get; set; }
    }

    private sealed class NotificationDayCountRow
    {
        public Guid AccountId { get; set; }
        public int Days { get; set; }
    }
}
