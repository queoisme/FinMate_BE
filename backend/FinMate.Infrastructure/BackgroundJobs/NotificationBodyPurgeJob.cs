using FinMate.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// Chạy mỗi đêm. Nội dung gốc của thông báo ngân hàng (có số tiền, số dư, đôi khi số tài khoản) chỉ được giữ 90 ngày (NFR-SEC-23).
/// Sau đó giao dịch, kết quả AI và <c>content_hash</c> vẫn còn, chỉ mất chữ gốc. <c>RetryFailedNotificationJob</c> thử tối đa 3 lần,
/// xong từ lâu trước mốc này nên không bao giờ gặp một log đã mất nội dung.
/// </summary>
public class NotificationBodyPurgeJob
{
    public const int RetentionDays = 90;

    private readonly INotificationLogRepository _logs;
    private readonly ILogger<NotificationBodyPurgeJob> _logger;

    public NotificationBodyPurgeJob(INotificationLogRepository logs, ILogger<NotificationBodyPurgeJob> logger)
    {
        _logs = logs;
        _logger = logger;
    }

    public Task RunAsync(CancellationToken ct) => RunAsync(DateTimeOffset.UtcNow, ct);

    public async Task RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var purged = await _logs.PurgeBodiesOlderThanAsync(now.AddDays(-RetentionDays), ct);
        // Chỉ ghi số dòng: nội dung thông báo không bao giờ vào log.
        _logger.LogInformation("Xóa nội dung thông báo quá {Days} ngày: {Count} dòng", RetentionDays, purged);
    }
}
