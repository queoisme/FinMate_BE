using FinMate.Application.Ops;
using FinMate.Application.Support;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>Chạy mỗi đêm. Ảnh khách gửi vào ticket chỉ được giữ có thời hạn sau khi ca đóng (NFR-SEC-24).</summary>
public class SupportRetentionJob
{
    private readonly ISupportRetentionService _retention;
    private readonly IOpsNotificationService _notifications;
    private readonly ILogger<SupportRetentionJob> _logger;

    public SupportRetentionJob(ISupportRetentionService retention, IOpsNotificationService notifications, ILogger<SupportRetentionJob> logger)
    {
        _retention = retention;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var (closed, deleted) = await _retention.RunAsync(DateTimeOffset.UtcNow, ct);
        _logger.LogInformation("Dọn ticket: đóng {Closed} ca, xóa {Deleted} ảnh", closed, deleted);

        // Chung job đêm với ticket để khỏi thêm một lịch Hangfire cho một câu DELETE.
        var purged = await _notifications.PurgeAsync(DateTimeOffset.UtcNow, ct);
        _logger.LogInformation("Dọn thông báo vận hành: xóa {Purged} dòng", purged);
    }
}
