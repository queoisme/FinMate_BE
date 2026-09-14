using System.Text.RegularExpressions;
using FirebaseAdmin.Messaging;

namespace FinMate.Infrastructure.ExternalServices;

/// <summary>
/// Cài đặt thật của <see cref="IFcmSender"/>. Chỉ dịch dữ liệu — mọi quyết định nghiệp vụ
/// nằm ở <see cref="FcmPushNotificationService"/>.
/// </summary>
public partial class FirebaseAdminFcmSender : IFcmSender
{
    /// <summary>FCM nhận tối đa 500 token mỗi lần gọi.</summary>
    private const int BatchSize = 500;

    public async Task<IReadOnlyList<FcmSendOutcome>> SendAsync(
        IReadOnlyList<string> tokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken ct = default)
    {
        var outcomes = new List<FcmSendOutcome>(tokens.Count);

        foreach (var batch in tokens.Chunk(BatchSize))
        {
            var message = new MulticastMessage
            {
                // "Fids" (Firebase Installation IDs) là tên mới của đúng cái cũ gọi là
                // Tokens — SDK 3.6 đổi tên và đánh dấu Tokens là deprecated.
                Fids = batch,
                Notification = new Notification { Title = title, Body = body },
                Data = data,
            };

            // SendEachForMulticast chứ không phải SendMulticast: bản "Each" gửi từng message
            // và trả kết quả RIÊNG cho mỗi token, nên một token chết không kéo cả lô hỏng
            // theo và ta biết đích danh token nào cần xoá.
            var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message, ct);

            for (var i = 0; i < batch.Length; i++)
            {
                var result = response.Responses[i];
                outcomes.Add(new FcmSendOutcome(
                    batch[i],
                    result.IsSuccess,
                    IsDeadToken(result.Exception),
                    FailureReasonOf(result.Exception)));
            }
        }

        return outcomes;
    }

    /// <summary>
    /// Chỉ hai mã này nghĩa là token hỏng vĩnh viễn. Mọi lỗi khác (mất mạng, FCM quá tải,
    /// hạn mức) là tạm thời — xoá token vì chúng là tự cắt đường tới thiết bị còn tốt.
    /// </summary>
    private static bool IsDeadToken(FirebaseMessagingException? exception)
        => exception?.MessagingErrorCode is MessagingErrorCode.Unregistered
            or MessagingErrorCode.SenderIdMismatch;

    /// <summary>
    /// Tên hằng của mã lỗi, KHÔNG phải toàn bộ Message. Khi lỗi thuộc về chính token, Message
    /// của FCM có thể nhắc lại token trong nội dung, mà token là capability không được phép
    /// rơi vào log.
    ///
    /// Lỗi xảy ra TRƯỚC khi gọi được FCM (credentials sai, không đổi được OAuth token) về đây
    /// với <c>ErrorCode = Unknown</c>, <c>MessagingErrorCode = null</c> và
    /// <c>InnerException = null</c> — SDK gói exception gốc vào phần ĐẦU của Message thay vì
    /// giữ nó làm inner. Một mình "Unknown" thì vô dụng, nên lấy thêm đúng cái tên kiểu ở đầu
    /// chuỗi đó: nó phân biệt ngay "sai credentials" (TokenResponseException) với "mất mạng"
    /// (HttpRequestException), và một tên kiểu thì không mang dữ liệu người dùng nào.
    /// </summary>
    private static string? FailureReasonOf(FirebaseMessagingException? exception)
    {
        if (exception is null)
        {
            return null;
        }

        var code = exception.MessagingErrorCode?.ToString() ?? exception.ErrorCode.ToString();
        var cause = exception.InnerException?.GetType().Name ?? LeadingExceptionTypeName(exception.Message);

        return cause is null ? code : $"{code} ({cause})";
    }

    /// <summary>
    /// Tên kiểu exception ở ĐẦU chuỗi, hoặc null. Chỉ khớp phần <c>Some.Name.Exception</c>
    /// mở đầu rồi dừng — phần mô tả phía sau không bao giờ được lấy theo.
    /// </summary>
    internal static string? LeadingExceptionTypeName(string message)
    {
        var match = LeadingExceptionType().Match(message);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"^([A-Za-z0-9_.]*Exception)\b")]
    private static partial Regex LeadingExceptionType();
}
