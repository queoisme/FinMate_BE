using System.Net.Http.Json;
using System.Text.Json;
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

    /// <summary>
    /// Chỉ <c>code</c> và <c>message</c> từ phản hồi lỗi, không bao giờ nhiều hơn.
    ///
    /// Bóc theo tên trường chứ không ghi cả body: nếu một ngày nào đó Brevo đổi và dội payload
    /// kèm cả trong phản hồi lỗi, cách này vẫn không để nội dung email lọt vào log.
    /// </summary>
    internal static string DescribeFailure(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "(no detail)";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var code = root.TryGetProperty("code", out var c) ? c.GetString() : null;
            var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;

            return (code, message) switch
            {
                (null, null) => "(no detail)",
                (not null, null) => code!,
                (null, not null) => message!,
                _ => $"{code}: {message}",
            };
        }
        catch (JsonException)
        {
            // Phản hồi không phải JSON — không đoán, và tuyệt đối không ghi nguyên văn.
            return "(unparseable response)";
        }
    }

    private static async Task<string> ReadFailureReasonAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return DescribeFailure(await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception)
        {
            return "(no detail)";
        }
    }

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

            // KHÔNG ghi nguyên body: ở nhánh thành công Brevo dội lại payload, mà payload là
            // nội dung email — nghĩa là chứa cả mã OTP. Nhưng ở nhánh LỖI thì phản hồi chỉ có
            // `code` và `message`, nên bóc đúng hai trường đó là an toàn và đáng giá: lỗi thật
            // gặp phải là Brevo chặn theo IP, và một dòng "HTTP 401" trần không nói được điều
            // đó — phải gọi tay sang Brevo mới biết.
            _logger.LogWarning(
                "Brevo refused the message for {ToEmail}: HTTP {StatusCode} {Reason}",
                toEmail, (int)response.StatusCode, await ReadFailureReasonAsync(response, ct));
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
