using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Monitoring;

public record ServiceStatusDto(string Service, string State, int ConsecutiveFailures, DateTimeOffset LastCheckedAt, DateTimeOffset? LastHealthyAt);

public record MonitoringStatusDto(IReadOnlyList<ServiceStatusDto> Services, int ActiveIncidents);

/// <param name="TechnicalDetail">Chỉ Admin thấy; với người khác luôn null.</param>
public record OpsIncidentDto(
    Guid Id,
    string Source,
    string Service,
    string Severity,
    string Status,
    string Title,
    string Impact,
    string? TechnicalDetail,
    int OccurrenceCount,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    Guid? AcknowledgedByUserId,
    DateTimeOffset? AcknowledgedAt,
    Guid? ResolvedByUserId,
    DateTimeOffset? ResolvedAt,
    string? ResolutionNote);

public record OpsIncidentPageDto(IReadOnlyList<OpsIncidentDto> Items, int Total, int Offset, int Limit);

public static class MonitoringErrorCodes
{
    public const string AlreadyResolved = "MONITORING_INCIDENT_RESOLVED";
    public const string InvalidInput = "MONITORING_INVALID_INPUT";
}

/// <summary>Thao tác của con người trên sự cố. Quyền đọc từ DB mỗi lần qua <see cref="IOpsAccessService"/>.</summary>
public interface IOpsMonitoringService
{
    Task<MonitoringStatusDto> GetStatusAsync(Guid actorId, CancellationToken ct = default);
    Task<OpsIncidentPageDto> ListAsync(Guid actorId, IncidentStatus? status, IncidentSeverity? severity, int offset, int limit, CancellationToken ct = default);
    Task<OpsIncidentDto> GetAsync(Guid actorId, Guid incidentId, CancellationToken ct = default);
    Task<OpsIncidentDto> AcknowledgeAsync(Guid actorId, Guid incidentId, string? ip, CancellationToken ct = default);
    Task<OpsIncidentDto> ResolveAsync(Guid actorId, Guid incidentId, string? note, string? ip, CancellationToken ct = default);
}

public sealed class OpsMonitoringService : IOpsMonitoringService
{
    private const int MaxPageSize = 100;
    private const int MaxNoteLength = 1000;

    private readonly IOpsAccessService _access;
    private readonly IMonitoringRepository _repository;
    private readonly IAuditLogService _audit;

    public OpsMonitoringService(IOpsAccessService access, IMonitoringRepository repository, IAuditLogService audit)
    {
        _access = access;
        _repository = repository;
        _audit = audit;
    }

    public async Task<MonitoringStatusDto> GetStatusAsync(Guid actorId, CancellationToken ct = default)
    {
        await _access.RequireAsync(actorId, OpsCapability.ViewMonitoring, ct);
        var statuses = await _repository.ListStatusesAsync(ct);
        var (_, active) = await _repository.ListIncidentsAsync(new IncidentFilter(null, null, ActiveOnly: true, 0, 1), ct);
        return new MonitoringStatusDto(
            statuses.Select(s => new ServiceStatusDto(s.Service, s.State.ToString(), s.ConsecutiveFailures, s.LastCheckedAt, s.LastHealthyAt)).ToList(),
            active);
    }

    public async Task<OpsIncidentPageDto> ListAsync(Guid actorId, IncidentStatus? status, IncidentSeverity? severity, int offset, int limit, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.ViewMonitoring, ct);
        var cleanOffset = Math.Max(offset, 0);
        var cleanLimit = Math.Clamp(limit, 1, MaxPageSize);
        var (items, total) = await _repository.ListIncidentsAsync(new IncidentFilter(status, severity, ActiveOnly: status is null, cleanOffset, cleanLimit), ct);
        return new OpsIncidentPageDto(items.Select(i => ToDto(i, actor)).ToList(), total, cleanOffset, cleanLimit);
    }

    public async Task<OpsIncidentDto> GetAsync(Guid actorId, Guid incidentId, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.ViewMonitoring, ct);
        var incident = await _repository.GetIncidentAsync(incidentId, ct) ?? throw new NotFoundException("OpsIncident", incidentId);
        return ToDto(incident, actor);
    }

    public async Task<OpsIncidentDto> AcknowledgeAsync(Guid actorId, Guid incidentId, string? ip, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.ManageIncidents, ct);
        var incident = await _repository.GetIncidentAsync(incidentId, ct) ?? throw new NotFoundException("OpsIncident", incidentId);
        EnsureNotResolved(incident);

        // Người đầu tiên giữ tên; người sau bấm lại không đổi gì và không ghi nhật ký.
        if (incident.Status == IncidentStatus.Open)
        {
            incident.Status = IncidentStatus.Acknowledged;
            incident.AcknowledgedByUserId = actor.Id;
            incident.AcknowledgedAt = DateTimeOffset.UtcNow;
            await _repository.SaveChangesAsync(ct);
            await _audit.LogAsync(AuditEvents.OpsIncidentAcknowledged, actor.Id, ip, new { incidentId }, ct);
        }

        return ToDto(incident, actor);
    }

    public async Task<OpsIncidentDto> ResolveAsync(Guid actorId, Guid incidentId, string? note, string? ip, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.ManageIncidents, ct);
        var incident = await _repository.GetIncidentAsync(incidentId, ct) ?? throw new NotFoundException("OpsIncident", incidentId);
        EnsureNotResolved(incident);

        var cleanNote = note?.Trim();
        if (cleanNote?.Length > MaxNoteLength)
        {
            throw new BusinessRuleException(MonitoringErrorCodes.InvalidInput, $"Ghi chú tối đa {MaxNoteLength} ký tự.");
        }

        incident.Status = IncidentStatus.Resolved;
        incident.ResolvedByUserId = actor.Id;
        incident.ResolvedAt = DateTimeOffset.UtcNow;
        incident.ResolutionNote = string.IsNullOrEmpty(cleanNote) ? null : cleanNote;
        await _repository.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.OpsIncidentResolved, actor.Id, ip, new { incidentId }, ct);
        return ToDto(incident, actor);
    }

    private static void EnsureNotResolved(OpsIncident incident)
    {
        if (incident.Status == IncidentStatus.Resolved)
        {
            throw new BusinessRuleException(MonitoringErrorCodes.AlreadyResolved, "Sự cố này đã được đóng.");
        }
    }

    private static OpsIncidentDto ToDto(OpsIncident i, User actor)
        => new(
            i.Id,
            i.Source.ToString(),
            i.Service,
            i.Severity.ToString(),
            i.Status.ToString(),
            i.Title,
            MonitoredServices.ImpactOf(i.Service),
            OpsPermissions.For(actor.Role).HasFlag(OpsCapability.ManageSystem) ? i.TechnicalDetail : null,
            i.OccurrenceCount,
            i.FirstSeenAt,
            i.LastSeenAt,
            i.AcknowledgedByUserId,
            i.AcknowledgedAt,
            i.ResolvedByUserId,
            i.ResolvedAt,
            i.ResolutionNote);
}
