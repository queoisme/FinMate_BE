namespace FinMate.Infrastructure.ExternalServices;

/// <param name="TokenIsDead">
/// FCM khẳng định token này không còn tồn tại (app bị gỡ, dữ liệu bị xoá, token hết hạn).
/// Khác hẳn "gửi hỏng": lỗi mạng hay FCM quá tải thì token vẫn tốt và phải giữ lại.
/// </param>
/// <param name="FailureReason">
/// Mã lỗi của FCM khi không gửi được, ví dụ <c>Unauthenticated</c> hay <c>QuotaExceeded</c>.
/// Chỉ là tên hằng của SDK — không chứa token, không chứa nội dung thông báo — nên an toàn
/// để ghi log.
/// </param>
public record FcmSendOutcome(string Token, bool Delivered, bool TokenIsDead, string? FailureReason = null);

/// <summary>
/// Lớp mỏng bọc quanh FirebaseMessaging.
///
/// Tồn tại chỉ vì FirebaseAdmin phơi ra API static (<c>FirebaseMessaging.DefaultInstance</c>)
/// không mock được. Toàn bộ phần đáng test — tôn trọng tuỳ chọn thông báo, dọn token chết,
/// nuốt lỗi — nằm ở <see cref="FcmPushNotificationService"/>; ở đây chỉ còn phép dịch giữa
/// hai kiểu dữ liệu, không có nhánh nghiệp vụ nào.
/// </summary>
public interface IFcmSender
{
    Task<IReadOnlyList<FcmSendOutcome>> SendAsync(
        IReadOnlyList<string> tokens, string title, string body, CancellationToken ct = default);
}
