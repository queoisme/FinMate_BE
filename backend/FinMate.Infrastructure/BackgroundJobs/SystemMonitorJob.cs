using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Monitoring;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>Chạy mỗi phút. Không retry: lượt kế tiếp tới ngay sau một phút. Lượt trước còn chạy thì lượt sau bỏ qua, để probe chậm không dồn lại.</summary>
public class SystemMonitorJob
{
    private readonly ISystemMonitor _monitor;

    public SystemMonitorJob(ISystemMonitor monitor)
    {
        _monitor = monitor;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public Task RunAsync(CancellationToken ct) => _monitor.RunAsync(DateTimeOffset.UtcNow, ct);
}

/// <summary>Chạy 08:00 giờ VN: gửi email gom các sự cố mức thường và dọn lỗi quá hạn.</summary>
public class MonitoringDigestJob
{
    private readonly IIncidentNotifier _notifier;
    private readonly IMonitoringRepository _repository;
    private readonly ISystemConfigProvider _config;
    private readonly ILogger<MonitoringDigestJob> _logger;

    public MonitoringDigestJob(IIncidentNotifier notifier, IMonitoringRepository repository, ISystemConfigProvider config, ILogger<MonitoringDigestJob> logger)
    {
        _notifier = notifier;
        _repository = repository;
        _config = config;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await _notifier.SendDigestAsync(now, ct);

        var settings = (await _config.GetAsync(ct)).Document.Monitoring;
        var deleted = await _repository.DeleteErrorEventsBeforeAsync(now.AddDays(-settings.ErrorRetentionDays), ct);
        _logger.LogInformation("Dọn lỗi quá hạn: xóa {Deleted} bản ghi", deleted);
    }
}
