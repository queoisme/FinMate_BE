namespace FinMate.Application.Auth;

/// <param name="RequireEmailVerification">
/// Chặn đăng nhập khi email chưa xác minh. Mặc định BẬT ở production.
///
/// Tắt được vì 23 file test tích hợp gọi <c>auth/register</c> rồi đăng nhập ngay; bắt tất cả
/// diễn lại màn OTP chỉ tạo nhiễu trong khi chúng đang kiểm thứ khác. Một nhóm test riêng bật
/// cờ này lên để phần chặn vẫn được kiểm thật.
/// </param>
public record AuthOptions(bool RequireEmailVerification);
