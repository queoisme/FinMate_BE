using FinMate.Application.MarketData;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>Đọc các nguồn thị trường. Không retry: lỗi của từng nguồn đã được ghi lại, và 3 giờ sau có lượt mới.</summary>
public class MarketDataRefreshJob
{
    private readonly IMarketDataService _service;
    private readonly ILogger<MarketDataRefreshJob> _logger;

    public MarketDataRefreshJob(IMarketDataService service, ILogger<MarketDataRefreshJob> logger)
    {
        _service = service;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task RunAsync(CancellationToken ct)
    {
        var summary = await _service.RefreshAsync(ct);
        var purged = await _service.PurgeOldDocumentsAsync(ct);
        _logger.LogInformation(
            "Market data refreshed: {Checked} sources, {Failed} failed, {NewDocuments} new documents, {Confirmed} fuels changed, {Purged} documents purged",
            summary.SourcesChecked, summary.SourcesFailed, summary.NewDocuments, summary.ConfirmedFuels.Count, purged);
    }
}
