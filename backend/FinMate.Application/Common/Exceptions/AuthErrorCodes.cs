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
}
