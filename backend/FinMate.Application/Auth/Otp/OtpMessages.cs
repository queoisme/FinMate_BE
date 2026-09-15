namespace FinMate.Application.Auth.Otp;

/// <summary>Nội dung email. Text thuần: template HTML sai lại dễ rơi vào Spam hơn.</summary>
public static class OtpMessages
{
    public static (string Subject, string Body) VerifyEmail(string code) => (
        "FinMate — Mã xác minh email",
        $"""
        Chào bạn,

        Mã xác minh tài khoản FinMate của bạn là: {code}

        Mã có hiệu lực trong 10 phút và chỉ dùng được một lần.
        Nếu bạn không đăng ký FinMate, hãy bỏ qua email này.
        """);

    public static (string Subject, string Body) ResetPassword(string code) => (
        "FinMate — Mã đặt lại mật khẩu",
        $"""
        Chào bạn,

        Mã đặt lại mật khẩu FinMate của bạn là: {code}

        Mã có hiệu lực trong 10 phút và chỉ dùng được một lần.
        Nếu bạn không yêu cầu đặt lại mật khẩu, hãy bỏ qua email này — mật khẩu hiện tại
        của bạn vẫn còn nguyên.
        """);
}
