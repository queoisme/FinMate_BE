namespace FinMate.Domain.Enums;

/// <summary>
/// Nền tảng của thiết bị đã đăng ký nhận push.
///
/// FCM không cần biết giá trị này để gửi — token đã đủ. Lưu để về sau còn trả lời được
/// những câu như "thiết bị iOS có nhận được cảnh báo không", và vì payload của hai nền tảng
/// khác nhau ở phần hiển thị nếu sau này cần tùy biến.
/// </summary>
public enum DevicePlatform
{
    Android,
    Ios,
    Web,
}
