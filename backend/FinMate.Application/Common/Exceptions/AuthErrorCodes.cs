namespace FinMate.Application.Common.Exceptions;

public static class AuthErrorCodes
{
    public const string InvalidCredentials = "AUTH_INVALID_CREDENTIALS";
    public const string AccountLocked = "AUTH_ACCOUNT_LOCKED";
    public const string TokenExpired = "AUTH_TOKEN_EXPIRED";
    public const string TokenInvalid = "AUTH_TOKEN_INVALID";
    public const string TokenReuseDetected = "AUTH_TOKEN_REUSE_DETECTED";
    public const string EmailAlreadyExists = "AUTH_EMAIL_ALREADY_EXISTS";

    /// <summary>Đăng nhập khi chưa xác minh email, và hệ thống đang bắt buộc xác minh.</summary>
    public const string EmailNotVerified = "AUTH_EMAIL_NOT_VERIFIED";

    /// <summary>Mã OTP sai, đã hết hạn, đã dùng, hoặc chưa từng được gửi.</summary>
    public const string OtpInvalid = "AUTH_OTP_INVALID";

    /// <summary>Sai quá số lần cho phép — mã bị huỷ, phải xin mã mới.</summary>
    public const string OtpTooManyAttempts = "AUTH_OTP_TOO_MANY_ATTEMPTS";

    /// <summary>Xin mã mới quá sớm. Giới hạn theo EMAIL, không chỉ theo IP.</summary>
    public const string OtpRequestedTooSoon = "AUTH_OTP_REQUESTED_TOO_SOON";

    /// <summary>App gửi lên phiên bản câu chữ đồng ý khác phiên bản hiện hành của server (app cũ hoặc câu chữ vừa đổi).</summary>
    public const string ConsentVersionOutdated = "AUTH_CONSENT_VERSION_OUTDATED";

    /// <summary>Loại đồng ý không tồn tại (giá trị enum ngoài danh sách).</summary>
    public const string ConsentKindUnknown = "AUTH_CONSENT_KIND_UNKNOWN";

    /// <summary>Điều khoản chung không rút lại được (muốn thôi thì xoá tài khoản).</summary>
    public const string ConsentNotWithdrawable = "AUTH_CONSENT_NOT_WITHDRAWABLE";

    /// <summary>Thiếu GOOGLE_CLIENT_SECRET nên luồng OAuth qua trình duyệt chưa bật được.</summary>
    public const string GoogleOAuthNotConfigured = "AUTH_GOOGLE_OAUTH_NOT_CONFIGURED";
}
