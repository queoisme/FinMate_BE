using FinMate.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.ExternalServices;

/// <summary>
/// Bản dùng khi chưa cấu hình Brevo: in email ra log thay vì gửi đi.
///
/// CÓ in cả nội dung, khác hẳn luật của push. Cố ý: không có nó thì máy dev và bộ test không
/// có cách nào lấy được mã OTP để đi tiếp. Đánh đổi chỉ chấp nhận được vì lớp này chỉ chạy khi
/// KHÔNG có API key — tức là không phải môi trường thật.
/// </summary>
public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task<bool> SendAsync(
        string toEmail, string subject, string body, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Would send email to {ToEmail} — {Subject}\n{Body}", toEmail, subject, body);
        return Task.FromResult(true);
    }
}
