using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// Mỗi đêm: cập nhật khoản định kỳ đã có (thấy thanh toán thì đẩy ngày đến hạn) và đề xuất khoản mới (sơ đồ 5a). Chỉ chạm user có giao dịch
/// trong cửa sổ lịch sử. Lỗi của một user không chặn những user còn lại.
/// </summary>
public class RecurringDetectionJob
{
    private readonly IReportRepository _reports;
    private readonly IRecurringDetectionService _detection;
    private readonly ISystemConfigProvider _config;
    private readonly ILogger<RecurringDetectionJob> _logger;

    public RecurringDetectionJob(
        IReportRepository reports, IRecurringDetectionService detection, ISystemConfigProvider config, ILogger<RecurringDetectionJob> logger)
    {
        _reports = reports;
        _detection = detection;
        _config = config;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var settings = (await _config.GetAsync(ct)).Document.Forecast;
        var today = VietnamTime.Today();
        var userIds = await _reports.GetUserIdsWithRecentActivityAsync(today.AddDays(-settings.HistoryWindowDays), today, ct);

        var created = 0;
        foreach (var userId in userIds)
        {
            try
            {
                created += await _detection.RunForUserAsync(userId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Recurring detection failed for user {UserId}", userId);
            }
        }

        _logger.LogInformation("Recurring detection finished: {Users} users, {Created} new items", userIds.Count, created);
    }
}
