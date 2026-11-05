using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.Monitoring;

public sealed record ProbeResult(MonitoredServiceState State, string? Detail = null);

/// <summary>Kiểm tra một dịch vụ. Không được ném lỗi vì dịch vụ hỏng: đó là kết quả, không phải sự cố của lượt kiểm tra.</summary>
public interface IServiceProbe
{
    MonitoredService Service { get; }
    Task<ProbeResult> ProbeAsync(CancellationToken ct = default);
}

/// <summary>Một nguồn phát hiện lỗi. Mỗi nguồn chạy độc lập, một nguồn hỏng không chặn nguồn khác.</summary>
public interface IMonitorSource
{
    Task RunAsync(MonitoringSettings settings, DateTimeOffset now, CancellationToken ct = default);
}

public interface ISystemMonitor
{
    Task RunAsync(DateTimeOffset now, CancellationToken ct = default);
}

public sealed class SystemMonitor : ISystemMonitor
{
    private readonly IEnumerable<IMonitorSource> _sources;
    private readonly IIncidentNotifier _notifier;
    private readonly IMonitoringRepository _repository;
    private readonly ISystemConfigProvider _config;
    private readonly ILogger<SystemMonitor> _logger;

    public SystemMonitor(IEnumerable<IMonitorSource> sources, IIncidentNotifier notifier, IMonitoringRepository repository, ISystemConfigProvider config, ILogger<SystemMonitor> logger)
    {
        _sources = sources;
        _notifier = notifier;
        _repository = repository;
        _config = config;
        _logger = logger;
    }

    public async Task RunAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var settings = (await _config.GetAsync(ct)).Document.Monitoring;

        foreach (var source in _sources)
        {
            try
            {
                await source.RunAsync(settings, now, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _repository.ResetTracking();
                _logger.LogError(ex, "Nguồn giám sát {Source} lỗi", source.GetType().Name);
            }
        }

        await _notifier.SendDueAsync(settings, now, ct);
    }
}

/// <summary>Theo dõi trạng thái liên tiếp của từng dịch vụ, để một lần chập chờn không thành cảnh báo.</summary>
public sealed class ServiceStatusTracker
{
    private readonly IMonitoringRepository _repository;
    private readonly IIncidentService _incidents;

    public ServiceStatusTracker(IMonitoringRepository repository, IIncidentService incidents)
    {
        _repository = repository;
        _incidents = incidents;
    }

    public async Task ApplyAsync(
        MonitoredService service,
        IncidentSource source,
        ProbeResult result,
        int failureThreshold,
        int recoveryThreshold,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var status = await _repository.GetStatusAsync(service.Key, ct);
        if (status is null)
        {
            status = new OpsServiceStatus { Service = service.Key };
            _repository.AddStatus(status);
        }

        status.State = result.State;
        status.Detail = result.Detail;
        status.LastCheckedAt = now;

        switch (result.State)
        {
            case MonitoredServiceState.Healthy:
                status.ConsecutiveFailures = 0;
                status.ConsecutiveSuccesses++;
                status.LastHealthyAt = now;
                break;
            case MonitoredServiceState.Unhealthy:
                status.ConsecutiveSuccesses = 0;
                status.ConsecutiveFailures++;
                break;
            default:
                status.ConsecutiveFailures = 0;
                status.ConsecutiveSuccesses = 0;
                break;
        }

        await _repository.SaveChangesAsync(ct);

        var fingerprint = $"{source.ToString().ToLowerInvariant()}:{service.Key}";
        if (result.State == MonitoredServiceState.Unhealthy && status.ConsecutiveFailures >= failureThreshold)
        {
            await _incidents.RaiseAsync(
                new IncidentSignal(fingerprint, source, service.Key, service.Severity, $"{service.Name} đang lỗi", result.Detail),
                occurrences: 1,
                now,
                ct);
        }
        else if (result.State == MonitoredServiceState.Disabled
            || (result.State == MonitoredServiceState.Healthy && status.ConsecutiveSuccesses >= recoveryThreshold))
        {
            await _incidents.ResolveAsync(fingerprint, now, ct);
        }
    }
}

/// <summary>Gọi từng probe và cập nhật trạng thái.</summary>
public sealed class HealthMonitorSource : IMonitorSource
{
    private readonly IEnumerable<IServiceProbe> _probes;
    private readonly ServiceStatusTracker _tracker;

    public HealthMonitorSource(IEnumerable<IServiceProbe> probes, ServiceStatusTracker tracker)
    {
        _probes = probes;
        _tracker = tracker;
    }

    public async Task RunAsync(MonitoringSettings settings, DateTimeOffset now, CancellationToken ct = default)
    {
        // Gọi song song để một dịch vụ treo tới hết timeout không làm chậm cả lượt; ghi DB thì tuần tự vì DbContext không an toàn đa luồng.
        var results = await Task.WhenAll(_probes.Select(async probe => (probe.Service, Result: await probe.ProbeAsync(ct))));
        foreach (var (service, result) in results)
        {
            await _tracker.ApplyAsync(service, IncidentSource.Health, result, settings.ProbeFailureThreshold, settings.ProbeRecoveryThreshold, now, ct);
        }
    }
}
