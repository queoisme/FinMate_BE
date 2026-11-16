using FinMate.Application.Common;
using FinMate.Application.Forecasting;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// Mỗi đêm sau khi nhận diện khoản định kỳ: (1) điền số dư thực tế cho các bản chụp đã tới ngày của mốc, (2) chụp dự báo hôm nay của mọi người có
/// hoạt động gần đây. Điền trước khi chụp để một lỗi ở bước chụp không làm chậm việc có số đo. Kết quả hiện ở trang độ chính xác của OpsManager.
/// </summary>
public class ForecastAccuracyJob(IForecastAccuracyService accuracy, ILogger<ForecastAccuracyJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var today = VietnamTime.Today();
        var filled = await accuracy.FillActualsAsync(today, ct);
        var created = await accuracy.CaptureAsync(today, ct);
        logger.LogInformation("Forecast accuracy job finished: {Filled} actuals filled, {Created} snapshots created", filled, created);
    }
}
