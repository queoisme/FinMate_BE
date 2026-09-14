using FinMate.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.ExternalServices;

/// <summary>
/// Đẩy thông báo thật qua FCM tới mọi thiết bị của người dùng.
///
/// Đây là chốt chặn DUY NHẤT cho <c>NotificationPrefs.PushEnabled</c>. Trước khi có FCM thật,
/// hai trong bốn chỗ gọi push quên kiểm tra cờ này — vô hại khi mọi thứ chỉ ghi log, nhưng
/// bật kênh thật lên là người đã tắt thông báo bắt đầu nhận. Kiểm ở đây thì không call site
/// mới nào có thể quên. Cờ hẹp hơn (ví dụ <c>BudgetAlertsEnabled</c>) vẫn thuộc về nơi biết
/// loại thông báo — xem <see cref="BudgetAlertNotifier"/>.
/// </summary>
public class FcmPushNotificationService : IPushNotificationService
{
    private readonly IUserRepository _userRepository;
    private readonly IDeviceTokenRepository _deviceTokenRepository;
    private readonly IFcmSender _sender;
    private readonly ILogger<FcmPushNotificationService> _logger;

    public FcmPushNotificationService(
        IUserRepository userRepository,
        IDeviceTokenRepository deviceTokenRepository,
        IFcmSender sender,
        ILogger<FcmPushNotificationService> logger)
    {
        _userRepository = userRepository;
        _deviceTokenRepository = deviceTokenRepository;
        _sender = sender;
        _logger = logger;
    }

    public async Task NotifyAsync(Guid userId, string title, string body, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is null || !user.NotificationPrefs.PushEnabled)
        {
            return;
        }

        var tokens = await _deviceTokenRepository.GetForUserAsync(userId, ct);
        if (tokens.Count == 0)
        {
            // Chuyện thường, không phải lỗi: người dùng đăng ký bằng web, hoặc đã đăng xuất
            // khỏi mọi máy.
            return;
        }

        IReadOnlyList<FcmSendOutcome> outcomes;
        try
        {
            outcomes = await _sender.SendAsync(tokens.Select(t => t.Token).ToList(), title, body, ct);
        }
        catch (Exception ex)
        {
            // KHÔNG ném lên. Mọi chỗ gọi push đều đứng sau một giao dịch đã lưu — ném ở đây
            // là để FCM sập kéo theo việc ghi nhận chi tiêu thất bại. Mất một thông báo là
            // chuyện nhỏ; mất một giao dịch thì không.
            _logger.LogError(ex, "FCM send failed for user {UserId} across {DeviceCount} device(s)",
                userId, tokens.Count);
            return;
        }

        var dead = outcomes.Where(o => o.TokenIsDead).Select(o => o.Token).ToList();
        if (dead.Count > 0)
        {
            await _deviceTokenRepository.RemoveManyAsync(dead, ct);
        }

        // Chỉ ghi SỐ LƯỢNG và MÃ LỖI. Không token (là capability đẩy được thông báo xuống
        // máy người khác) và không body (nhánh thông báo ngân hàng nhét số tiền vào đó —
        // xem luật "không bao giờ log amount_cents" ở CLAUDE.md).
        var delivered = outcomes.Count(o => o.Delivered);
        var reasons = outcomes
            .Where(o => !o.Delivered && o.FailureReason is not null)
            .Select(o => o.FailureReason!)
            .Distinct()
            .ToList();

        if (delivered == 0 && reasons.Count > 0)
        {
            // Không gửi được cho ai mà vẫn ghi Information thì hỏng này là hỏng im lặng:
            // triệu chứng duy nhất là người dùng không nhận được gì, không ai truy ra nổi.
            _logger.LogWarning(
                "Push to user {UserId} reached no device across {DeviceCount} token(s); FCM said {Reasons}",
                userId, outcomes.Count, string.Join(", ", reasons));
            return;
        }

        _logger.LogInformation(
            "Pushed to user {UserId}: {Delivered} delivered, {Dead} dead token(s) pruned",
            userId, delivered, dead.Count);
    }
}
