using System.Net.Http.Json;
using FinMate.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.ExternalServices;

/// <summary>
/// Gửi email qua Brevo bằng HttpClient trần.
///
/// Không dùng SDK của Brevo: API chỉ là một POST JSON kèm header <c>api-key</c>, mà kéo về cả
/// một SDK cho đúng một lời gọi là thêm dependency phải duyệt (AGENTS.md §5) đổi lấy rất ít.
/// Đổi sang nhà cung cấp khác sau này chỉ là viết lại lớp này.
/// </summary>
public class BrevoEmailSender : IEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly string _senderEmail;
    private readonly string _senderName;
    private readonly ILogger<BrevoEmailSender> _logger;

    public BrevoEmailSender(
        HttpClient httpClient, string senderEmail, string senderName, ILogger<BrevoEmailSender> logger)
    {
        _httpClient = httpClient;
        _senderEmail = senderEmail;
        _senderName = senderName;
        _logger = logger;
    }

    private record Party(string Email, string? Name = null);

    private record SendPayload(Party Sender, IReadOnlyList<Party> To, string Subject, string TextContent);

    public async Task<bool> SendAsync(
        string toEmail, string subject, string body, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "v3/smtp/email",
                new SendPayload(new Party(_senderEmail, _senderName), [new Party(toEmail)], subject, body),
                ct);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            // Ghi mã trạng thái, KHÔNG ghi body phản hồi: Brevo dội lại payload trong đó, mà
            // payload thì chứa nội dung email — nghĩa là chứa cả mã OTP.
            _logger.LogWarning(
                "Brevo refused the message for {ToEmail}: HTTP {StatusCode}",
                toEmail, (int)response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            // Không ném: caller là luồng đăng ký và quên mật khẩu. Brevo sập không được phép
            // làm hỏng việc đăng ký — người dùng bấm gửi lại là xong.
            _logger.LogError(ex, "Brevo request failed for {ToEmail}", toEmail);
            return false;
        }
    }
}
