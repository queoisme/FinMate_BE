using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// Chốt lại những ngày ĐÃ chốt mà một thao tác hoàn/đảo vừa đổi số (Phase 81, D7). Hôm nay và tương lai chưa được chốt nên bỏ qua: báo cáo tháng
/// đọc phần đó trực tiếp. Nuốt lỗi: tóm tắt là bản đệm, không được làm hỏng thao tác tiền đã lưu xong.
/// </summary>
public sealed class DailySummaryRefresher : IDailySummaryRefresher
{
    private readonly DailySummaryJob _job;
    private readonly ILogger<DailySummaryRefresher> _logger;

    public DailySummaryRefresher(DailySummaryJob job, ILogger<DailySummaryRefresher> logger)
    {
        _job = job;
        _logger = logger;
    }

    public async Task RefreshAsync(Guid userId, IReadOnlyCollection<DateOnly> dates, CancellationToken ct = default)
    {
        var today = VietnamTime.Today();
        foreach (var date in dates.Where(d => d < today).Distinct())
        {
            try
            {
                await _job.SummariseUserAsync(userId, date, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning("Daily summary refresh failed after a refund change: {ExceptionType}", ex.GetType().Name);
            }
        }
    }
}
