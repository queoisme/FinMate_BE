using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>Lượt kiểm tra hằng ngày lúc 08:00 VN (sơ đồ: "Đến lịch kiểm tra hằng ngày"): số dư thấp, vượt ngân sách sớm, hóa đơn, thu nhập trễ.</summary>
public class ForecastDailyAlertJob
{
    private readonly IReportRepository _reports;
    private readonly IForecastAlertService _alerts;
    private readonly ISystemConfigProvider _config;
    private readonly ILogger<ForecastDailyAlertJob> _logger;

    public ForecastDailyAlertJob(IReportRepository reports, IForecastAlertService alerts, ISystemConfigProvider config, ILogger<ForecastDailyAlertJob> logger)
    {
        _reports = reports;
        _alerts = alerts;
        _config = config;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var settings = (await _config.GetAsync(ct)).Document.Forecast;
        var today = VietnamTime.Today();
        var userIds = await _reports.GetUserIdsWithRecentActivityAsync(today.AddDays(-settings.HistoryWindowDays), today, ct);
        foreach (var userId in userIds)
        {
            try
            {
                await _alerts.RunDailyAsync(userId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Forecast daily alerts failed for user {UserId}", userId);
            }
        }
    }
}

/// <summary>Gửi các cảnh báo đã hẹn giờ (hết giờ yên lặng, hoặc cảnh báo của job đêm chờ tới sáng).</summary>
public class ForecastAlertDispatchJob
{
    private readonly IForecastAlertDispatcher _dispatcher;

    public ForecastAlertDispatchJob(IForecastAlertDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public Task RunAsync(CancellationToken ct = default) => _dispatcher.DispatchDueAsync(ct);
}

/// <summary>Kiểm tra một user ngay sau khi giao dịch của họ đổi (xếp hàng bởi <see cref="HangfireForecastAlertTrigger"/>).</summary>
public class ForecastUserCheckJob
{
    private readonly IForecastAlertService _alerts;

    public ForecastUserCheckJob(IForecastAlertService alerts)
    {
        _alerts = alerts;
    }

    public Task RunAsync(Guid userId, CancellationToken ct = default) => _alerts.CheckUserAsync(userId, ct);
}

/// <summary>Xếp hàng kiểm tra cảnh báo ra ngoài request. Lỗi xếp hàng chỉ ghi log: không bao giờ làm hỏng việc lưu giao dịch.</summary>
public class HangfireForecastAlertTrigger : IForecastAlertTrigger
{
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<HangfireForecastAlertTrigger> _logger;

    public HangfireForecastAlertTrigger(IBackgroundJobClient jobs, ILogger<HangfireForecastAlertTrigger> logger)
    {
        _jobs = jobs;
        _logger = logger;
    }

    public Task UserChangedAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            _jobs.Enqueue<ForecastUserCheckJob>(job => job.RunAsync(userId, CancellationToken.None));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not enqueue forecast check for user {UserId}", userId);
        }

        return Task.CompletedTask;
    }
}
