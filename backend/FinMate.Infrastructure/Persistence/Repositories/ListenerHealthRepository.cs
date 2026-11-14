using FinMate.Application.Common.Interfaces;
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

    public Task RecordReportAsync(ListenerReport r, CancellationToken ct = default)
        => _context.Database.ExecuteSqlInterpolatedAsync($"""
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
