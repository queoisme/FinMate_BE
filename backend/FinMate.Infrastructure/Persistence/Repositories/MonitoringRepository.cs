using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class MonitoringRepository : IMonitoringRepository
{
    private const string UniqueViolation = "23505";

    private readonly FinMateDbContext _context;

    public MonitoringRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<List<OpsServiceStatus>> ListStatusesAsync(CancellationToken ct = default)
        => _context.OpsServiceStatuses.AsNoTracking().OrderBy(s => s.Service).ToListAsync(ct);

    public Task<OpsServiceStatus?> GetStatusAsync(string service, CancellationToken ct = default)
        => _context.OpsServiceStatuses.FirstOrDefaultAsync(s => s.Service == service, ct);

    public void AddStatus(OpsServiceStatus status) => _context.OpsServiceStatuses.Add(status);

    public Task<OpsIncident?> GetActiveByFingerprintAsync(string fingerprint, CancellationToken ct = default)
        => _context.OpsIncidents.FirstOrDefaultAsync(i => i.Fingerprint == fingerprint && i.Status != IncidentStatus.Resolved, ct);

    public async Task<bool> TryAddIncidentAsync(OpsIncident incident, CancellationToken ct = default)
    {
        _context.OpsIncidents.Add(incident);
        try
        {
            await _context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _context.Entry(incident).State = EntityState.Detached;
            return false;
        }
    }

    public Task<OpsIncident?> GetIncidentAsync(Guid id, CancellationToken ct = default)
        => _context.OpsIncidents.FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<(List<OpsIncident> Items, int Total)> ListIncidentsAsync(IncidentFilter filter, CancellationToken ct = default)
    {
        var query = _context.OpsIncidents.AsNoTracking();
        if (filter.Status is { } status)
        {
            query = query.Where(i => i.Status == status);
        }
        else if (filter.ActiveOnly)
        {
            query = query.Where(i => i.Status != IncidentStatus.Resolved);
        }

        if (filter.Severity is { } severity)
        {
            query = query.Where(i => i.Severity == severity);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(i => i.LastSeenAt).ThenBy(i => i.Id).Skip(filter.Offset).Take(filter.Limit).ToListAsync(ct);
        return (items, total);
    }

    public Task<List<OpsIncident>> ListOpenUrgentAsync(CancellationToken ct = default)
        => _context.OpsIncidents
            .Where(i => i.Status == IncidentStatus.Open && i.Severity != IncidentSeverity.Normal)
            .ToListAsync(ct);

    public Task<List<OpsIncident>> ListOpenNormalAsync(CancellationToken ct = default)
        => _context.OpsIncidents
            .Where(i => i.Status == IncidentStatus.Open && i.Severity == IncidentSeverity.Normal)
            .OrderBy(i => i.FirstSeenAt)
            .ToListAsync(ct);

    public Task<List<OpsIncident>> ListResolvedUnnotifiedAsync(CancellationToken ct = default)
        => _context.OpsIncidents
            .Where(i => i.Status == IncidentStatus.Resolved && i.Severity != IncidentSeverity.Normal && i.NotificationCount > 0 && i.ResolvedNotifiedAt == null)
            .ToListAsync(ct);

    public Task<List<AlertRecipient>> ListAlertRecipientsAsync(bool includeManagers, CancellationToken ct = default)
        => _context.Users.AsNoTracking()
            .Where(u => !u.IsLocked && (u.Role == UserRole.Admin || (includeManagers && u.Role == UserRole.OpsManager)))
            .Select(u => new AlertRecipient(u.Id, u.Email))
            .ToListAsync(ct);

    public async Task<List<ReceiptScanCount>> CountReceiptScansAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        var rows = await _context.ReceiptScans.AsNoTracking()
            .Where(s => s.CreatedAt >= since)
            .GroupBy(s => new { s.OcrResult, s.OcrEngine })
            .Select(g => new { g.Key.OcrResult, g.Key.OcrEngine, Count = g.Count() })
            .ToListAsync(ct);
        return rows.Select(r => new ReceiptScanCount(r.OcrResult, r.OcrEngine, r.Count)).ToList();
    }

    public async Task<List<ErrorGroup>> GroupRecentErrorsAsync(IncidentSource source, DateTimeOffset since, CancellationToken ct = default)
    {
        var recent = _context.OpsErrorEvents.AsNoTracking().Where(e => e.Source == source && e.CreatedAt >= since);
        var counts = await recent.GroupBy(e => e.Fingerprint).Select(g => new { Fingerprint = g.Key, Count = g.Count() }).ToListAsync(ct);

        var groups = new List<ErrorGroup>(counts.Count);
        foreach (var count in counts)
        {
            var sample = await recent.Where(e => e.Fingerprint == count.Fingerprint).OrderByDescending(e => e.CreatedAt).FirstAsync(ct);
            groups.Add(new ErrorGroup(count.Fingerprint, sample.Service, count.Count, sample.ExceptionType, sample.SourceContext, sample.RequestPath));
        }

        return groups;
    }

    public Task<int> CountErrorsSinceAsync(string fingerprint, DateTimeOffset since, CancellationToken ct = default)
        => _context.OpsErrorEvents.CountAsync(e => e.Fingerprint == fingerprint && e.CreatedAt > since, ct);

    public Task<List<string>> ListQuietIncidentFingerprintsAsync(IncidentSource source, DateTimeOffset quietSince, CancellationToken ct = default)
        => _context.OpsIncidents.AsNoTracking()
            .Where(i => i.Source == source && i.Status != IncidentStatus.Resolved)
            .Where(i => !_context.OpsErrorEvents.Any(e => e.Fingerprint == i.Fingerprint && e.CreatedAt >= quietSince))
            .Select(i => i.Fingerprint)
            .ToListAsync(ct);

    public async Task<(List<OpsErrorEvent> Items, int Total)> ListErrorEventsAsync(string? service, string? exceptionType, DateTimeOffset? from, DateTimeOffset? to, int offset, int limit, CancellationToken ct = default)
    {
        var query = _context.OpsErrorEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(service))
        {
            query = query.Where(e => e.Service == service);
        }

        if (!string.IsNullOrWhiteSpace(exceptionType))
        {
            query = query.Where(e => e.ExceptionType == exceptionType);
        }

        if (from is { } start)
        {
            query = query.Where(e => e.CreatedAt >= start);
        }

        if (to is { } end)
        {
            query = query.Where(e => e.CreatedAt <= end);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id).Skip(offset).Take(limit).ToListAsync(ct);
        return (items, total);
    }

    public Task<int> DeleteErrorEventsBeforeAsync(DateTimeOffset before, CancellationToken ct = default)
        => _context.OpsErrorEvents.Where(e => e.CreatedAt < before).ExecuteDeleteAsync(ct);

    public void ResetTracking() => _context.ChangeTracker.Clear();

    public Task SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
}
