using FinMate.Application.MarketData;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>Đọc các nguồn thị trường. Không retry: lỗi của từng nguồn đã được ghi lại, và 3 giờ sau có lượt mới.</summary>
public class MarketDataRefreshJob
{
    private readonly IMarketDataService _service;
    private readonly IMarketAlertService _alerts;
    private readonly ILogger<MarketDataRefreshJob> _logger;

    public MarketDataRefreshJob(IMarketDataService service, IMarketAlertService alerts, ILogger<MarketDataRefreshJob> logger)
    {
        _alerts = alerts;
        _service = service;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task RunAsync(CancellationToken ct)
    {
        var summary = await _service.RefreshAsync(ct);
        var purged = await _service.PurgeOldDocumentsAsync(ct);
        await _alerts.ProcessDueAsync(ct);
        _logger.LogInformation(
            "Market data refreshed: {Checked} sources, {Failed} failed, {NewDocuments} new documents, {Confirmed} fuels changed, {Purged} documents purged",
            summary.SourcesChecked, summary.SourcesFailed, summary.NewDocuments, summary.ConfirmedFuels.Count, purged);
    }
}

/// <summary>Xét gửi cảnh báo ngay sau khi admin duyệt sự kiện hoặc sửa giá, thay vì chờ tới lượt đọc kế tiếp.</summary>
public class MarketAlertJob
{
    private readonly IMarketAlertService _alerts;

    public MarketAlertJob(IMarketAlertService alerts)
    {
        _alerts = alerts;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public Task RunAsync(CancellationToken ct) => _alerts.ProcessDueAsync(ct);
}

/// <summary>Lỗi xếp hàng chỉ ghi log: việc duyệt đã lưu, và lượt đọc kế tiếp vẫn xét gửi cảnh báo.</summary>
public sealed class HangfireMarketAlertTrigger : IMarketAlertTrigger
{
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<HangfireMarketAlertTrigger> _logger;

    public HangfireMarketAlertTrigger(IBackgroundJobClient jobs, ILogger<HangfireMarketAlertTrigger> logger)
    {
        _jobs = jobs;
        _logger = logger;
    }

    public void Enqueue()
    {
        try
        {
            _jobs.Enqueue<MarketAlertJob>(job => job.RunAsync(CancellationToken.None));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not enqueue market alerts: {ErrorType}", ex.GetType().Name);
        }
    }
}
