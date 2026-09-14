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

    public Task NotifyAsync(
        Guid userId,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken ct = default)
    {
        // Ghi cả title/body ở đây là có chủ ý và CHỈ an toàn vì đây là bản log-only dành cho
        // máy dev — xem FcmPushNotificationService để biết luật thật khi kênh gửi đã bật.
        // Riêng `data` thì không: nó mang số tiền dưới dạng có thể bóc ra được bằng máy,
        // và chỉ log KHOÁ để còn kiểm được client nhận đủ trường hay chưa.
        _logger.LogInformation(
            "Would push notification to user {UserId}: {Title} — {Body} [data: {DataKeys}]",
            userId, title, body, data is null ? "none" : string.Join(",", data.Keys));
        return Task.CompletedTask;
    }
}
