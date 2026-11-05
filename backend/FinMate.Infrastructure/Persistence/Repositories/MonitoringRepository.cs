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

    public Task<int> DeleteErrorEventsBeforeAsync(DateTimeOffset before, CancellationToken ct = default)
        => _context.OpsErrorEvents.Where(e => e.CreatedAt < before).ExecuteDeleteAsync(ct);

    public void ResetTracking() => _context.ChangeTracker.Clear();

    public Task SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
}
