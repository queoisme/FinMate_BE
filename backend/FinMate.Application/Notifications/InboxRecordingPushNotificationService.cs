using FinMate.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.Notifications;

/// <summary>
/// Chỗ chung mọi push đi qua: lưu push vào hộp thư trong app rồi mới đẩy, để người dùng lỡ vuốt mất push vẫn thấy lại trong màn
/// Thông báo. Lưu cả khi người dùng tắt push (chốt PushEnabled nằm sau, ở FcmPushNotificationService): tắt push là không muốn bị
/// làm phiền, không phải không muốn có lịch sử. Lỗi ghi hộp thư không được chặn push, và push luôn đứng sau một thay đổi đã lưu
/// nên không ném lỗi ra nơi gọi.
/// </summary>
public sealed class InboxRecordingPushNotificationService : IPushNotificationService
{
    private readonly IPushNotificationService _inner;
    private readonly IUserNotificationService _inbox;
    private readonly ILogger<InboxRecordingPushNotificationService> _logger;

    public InboxRecordingPushNotificationService(
        IPushNotificationService inner, IUserNotificationService inbox, ILogger<InboxRecordingPushNotificationService> logger)
    {
        _inner = inner;
        _inbox = inbox;
        _logger = logger;
    }

    public async Task NotifyAsync(
        Guid userId, string title, string body, IReadOnlyDictionary<string, string>? data = null, CancellationToken ct = default)
    {
        try
        {
            await _inbox.RecordPushAsync(userId, title, body, data, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Chỉ tên kiểu lỗi: body/data mang số tiền.
            _logger.LogWarning("Could not record push in the inbox for user {UserId} ({ErrorType})", userId, ex.GetType().Name);
        }

        await _inner.NotifyAsync(userId, title, body, data, ct);
    }
}
