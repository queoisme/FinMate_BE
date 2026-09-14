using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>
/// Một thiết bị đã đăng ký nhận push. Server không có đường nào tự chạm tới điện thoại —
/// FCM cấp cho mỗi lần cài app một token, và đây là chỗ duy nhất biết gửi cảnh báo đi đâu.
///
/// Một user có nhiều dòng (điện thoại, máy tính bảng). Một token thì chỉ thuộc về ĐÚNG MỘT
/// user — xem <c>uq_device_tokens_token</c> và ghi chú ở <c>RegisterDeviceTokenCommandHandler</c>.
/// </summary>
public class DeviceToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>
    /// Token FCM. Là một **capability**: ai cầm nó đều đẩy được thông báo xuống máy đó, nên
    /// không bao giờ đưa vào URL, log hay thông báo lỗi.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    public DevicePlatform Platform { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Lần cuối client khẳng định token này còn sống (mỗi lần mở app nó đăng ký lại).
    /// FCM thu hồi token của app lâu không mở, nên đây là thứ để dọn rác sau này.
    /// </summary>
    public DateTimeOffset LastSeenAt { get; set; }

    public User? User { get; set; }
}
