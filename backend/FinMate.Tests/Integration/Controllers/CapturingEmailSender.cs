using System.Collections.Concurrent;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Giữ lại email thay vì gửi đi, để test đọc được mã OTP và đi hết luồng thật.
///
/// Bản thật gửi qua Brevo; bản log-only in ra <c>ILogger</c> nên test không lấy lại được.
/// </summary>
public class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentDictionary<string, string> _bodyByEmail = new();

    public Task<bool> SendAsync(
        string toEmail, string subject, string body, CancellationToken ct = default)
    {
        _bodyByEmail[toEmail.ToLowerInvariant()] = body;
        return Task.FromResult(true);
    }

    public bool WasSentTo(string email) => _bodyByEmail.ContainsKey(email.ToLowerInvariant());

    /// <summary>Mã 6 chữ số trong email gửi tới địa chỉ này, hoặc null nếu chưa gửi gì.</summary>
    public string? CodeFor(string email)
    {
        if (!_bodyByEmail.TryGetValue(email.ToLowerInvariant(), out var body))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(body, @"\b\d{6}\b");
        return match.Success ? match.Value : null;
    }
}
