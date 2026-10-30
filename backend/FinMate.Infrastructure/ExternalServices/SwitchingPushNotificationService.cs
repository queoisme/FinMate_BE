using FinMate.Application.Common.Interfaces;

namespace FinMate.Infrastructure.ExternalServices;

/// <summary>
/// Chọn kênh đẩy tại lúc gửi: có credentials FCM hợp lệ (kho key hoặc biến môi trường) thì đẩy thật, không thì chỉ ghi log. Trước đây việc chọn này chốt một lần lúc khởi động nên đổi credentials phải
/// khởi động lại backend.
/// </summary>
public class SwitchingPushNotificationService : IPushNotificationService
{
    private readonly FcmApp _app;
    private readonly FcmPushNotificationService _fcm;
    private readonly LoggingPushNotificationService _logOnly;

    public SwitchingPushNotificationService(FcmApp app, FcmPushNotificationService fcm, LoggingPushNotificationService logOnly)
    {
        _app = app;
        _fcm = fcm;
        _logOnly = logOnly;
    }

    public async Task NotifyAsync(
        Guid userId, string title, string body, IReadOnlyDictionary<string, string>? data = null, CancellationToken ct = default)
    {
        if (await _app.EnsureAsync(ct))
        {
            await _fcm.NotifyAsync(userId, title, body, data, ct);
        }
        else
        {
            await _logOnly.NotifyAsync(userId, title, body, data, ct);
        }
    }
}
