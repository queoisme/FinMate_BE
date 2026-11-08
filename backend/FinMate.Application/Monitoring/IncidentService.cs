using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Monitoring;

public sealed record IncidentSignal(
    string Fingerprint,
    IncidentSource Source,
    string Service,
    IncidentSeverity Severity,
    string Title,
    string? TechnicalDetail);

/// <summary>Mở, gom và tự đóng sự cố. Đây là đường của hệ thống; thao tác của con người đi qua <see cref="IOpsMonitoringService"/>.</summary>
public interface IIncidentService
{
    /// <summary>Lỗi đã có sự cố đang mở thì chỉ tăng đếm; chưa có thì mở mới.</summary>
    Task RaiseAsync(IncidentSignal signal, int occurrences, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Tự đóng sự cố đang mở của lỗi này, nếu có.</summary>
    Task ResolveAsync(string fingerprint, DateTimeOffset now, CancellationToken ct = default);
}

public sealed class IncidentService : IIncidentService
{
    private readonly IMonitoringRepository _repository;

    public IncidentService(IMonitoringRepository repository)
    {
        _repository = repository;
    }

    public async Task RaiseAsync(IncidentSignal signal, int occurrences, DateTimeOffset now, CancellationToken ct = default)
    {
        var existing = await _repository.GetActiveByFingerprintAsync(signal.Fingerprint, ct);
        if (existing is null)
        {
            var added = await _repository.TryAddIncidentAsync(
                new OpsIncident
                {
                    Id = Guid.NewGuid(),
                    Fingerprint = signal.Fingerprint,
                    Source = signal.Source,
                    Service = signal.Service,
                    Severity = signal.Severity,
                    Status = IncidentStatus.Open,
                    Title = signal.Title,
                    TechnicalDetail = signal.TechnicalDetail,
                    OccurrenceCount = occurrences,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                },
                ct);
            if (added)
            {
                return;
            }

            existing = await _repository.GetActiveByFingerprintAsync(signal.Fingerprint, ct);
            if (existing is null)
            {
                return;
            }
        }

        existing.OccurrenceCount += occurrences;
        existing.LastSeenAt = now;
        existing.TechnicalDetail = signal.TechnicalDetail ?? existing.TechnicalDetail;

        // Nặng lên thì báo lại ngay dù đã có người nhận: người đó đang xử lý một việc nhỏ hơn thực tế.
        if (signal.Severity > existing.Severity)
        {
            existing.Severity = signal.Severity;
            existing.Status = IncidentStatus.Open;
            existing.LastNotifiedAt = null;
        }

        await _repository.SaveChangesAsync(ct);
    }

    public async Task ResolveAsync(string fingerprint, DateTimeOffset now, CancellationToken ct = default)
    {
        var incident = await _repository.GetActiveByFingerprintAsync(fingerprint, ct);
        if (incident is null)
        {
            return;
        }

        incident.Status = IncidentStatus.Resolved;
        incident.ResolvedAt = now;
        await _repository.SaveChangesAsync(ct);
    }
}
