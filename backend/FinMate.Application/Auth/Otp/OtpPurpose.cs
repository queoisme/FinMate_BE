namespace FinMate.Application.Auth.Otp;

/// <summary>
/// Mã sinh cho việc này KHÔNG dùng được cho việc kia — nó nằm trong khoá lưu trữ.
///
/// Thiếu tách biệt thì một mã xin được qua "gửi lại mã xác minh" sẽ đặt lại được mật khẩu,
/// tức là ai chạm được vào hộp thư một lần là đổi được cả hai.
/// </summary>
public enum OtpPurpose
{
    VerifyEmail,
    ResetPassword,

    /// <summary>Bước thứ hai khi tài khoản nội bộ đăng nhập.</summary>
    OpsLogin,

    /// <summary>Xác thực lại trước khi tạo gói dữ liệu cá nhân (Phase 91, BR-EXP-06).</summary>
    DataExport,
}
