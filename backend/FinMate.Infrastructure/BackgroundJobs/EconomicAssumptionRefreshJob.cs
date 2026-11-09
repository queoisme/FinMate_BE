using FinMate.Application.Economics;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>Mỗi tháng đọc thông cáo CPI mới nhất và cập nhật giả định lạm phát (xem <see cref="EconomicAssumptionRefresher"/>). Tắt khi chưa đặt địa chỉ nguồn trong cấu hình hệ thống.</summary>
public class EconomicAssumptionRefreshJob(EconomicAssumptionRefresher refresher, ILogger<EconomicAssumptionRefreshJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var outcome = await refresher.RunAsync(ct);
        logger.LogInformation("economic_assumption_refresh outcome={Outcome}", outcome);
    }
}
