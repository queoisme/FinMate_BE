namespace FinMate.Application.Common.Interfaces;

public interface IEmailSender
{
    /// <summary>
    /// Gửi một email văn bản thuần. Không ném khi nhà cung cấp lỗi — caller là luồng đăng ký
    /// hoặc quên mật khẩu, và để một sự cố bên thứ ba làm hỏng cả hai là đánh đổi tệ.
    /// Trả về việc gửi có thành công hay không để caller ghi log cho đúng.
    /// </summary>
    Task<bool> SendAsync(string toEmail, string subject, string body, CancellationToken ct = default);
}
