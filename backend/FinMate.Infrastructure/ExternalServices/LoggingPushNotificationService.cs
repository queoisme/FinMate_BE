using FinMate.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.ExternalServices;

// Chưa có push provider (FCM) được duyệt trong TECH_STACK.md — implement dạng log-only
// theo AGENTS.md §5 (không tự ý thêm dependency mới). Thay bằng implementation thật khi
// sản phẩm chốt provider (FCM/khác).
public class LoggingPushNotificationService : IPushNotificationService
{
    private readonly ILogger<LoggingPushNotificationService> _logger;

    public LoggingPushNotificationService(ILogger<LoggingPushNotificationService> logger)
    {
        _logger = logger;
    }

    public Task NotifyAsync(Guid userId, string title, string body, CancellationToken ct = default)
    {
        _logger.LogInformation("Would push notification to user {UserId}: {Title} — {Body}", userId, title, body);
        return Task.CompletedTask;
    }
}
