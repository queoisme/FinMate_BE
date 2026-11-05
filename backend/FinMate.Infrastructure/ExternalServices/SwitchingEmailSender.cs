using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Monitoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.ExternalServices;

/// <summary>
/// Gửi email qua Brevo bằng key ĐANG HIỆU LỰC tại lúc gửi (kho key trước, biến môi trường sau), không cố định lúc khởi động: admin đổi key trên trang quản trị là lần gửi kế tiếp dùng key mới.
/// Chưa có key hoặc chưa có <c>BREVO_SENDER_EMAIL</c> thì in mã ra log như <see cref="LoggingEmailSender"/>, nên máy dev và bộ test không cần tài khoản Brevo.
/// </summary>
public class SwitchingEmailSender : IEmailSender
{
    public const string HttpClientName = "brevo";

    private readonly ISecretVault _vault;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggers;
    private readonly LoggingEmailSender _fallback;

    private readonly IProviderFailureTracker? _failures;

    public SwitchingEmailSender(
        ISecretVault vault, IHttpClientFactory http, IConfiguration configuration, ILoggerFactory loggers, LoggingEmailSender fallback,
        IProviderFailureTracker? failures = null)
    {
        _failures = failures;
        _vault = vault;
        _http = http;
        _configuration = configuration;
        _loggers = loggers;
        _fallback = fallback;
    }

    public async Task<bool> SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        var key = await _vault.GetAsync(BackendSecrets.BrevoApiKey, ct);
        var senderEmail = _configuration["BREVO_SENDER_EMAIL"];
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(senderEmail))
        {
            return await _fallback.SendAsync(toEmail, subject, body, ct);
        }

        // Client mới mỗi lần (handler vẫn được gộp nhóm): header chứa key của LẦN NÀY, không dính sang lần khác.
        var client = _http.CreateClient(HttpClientName);
        client.DefaultRequestHeaders.Add("api-key", key);
        var sender = new BrevoEmailSender(client, senderEmail, _configuration["BREVO_SENDER_NAME"] ?? "FinMate", _loggers.CreateLogger<BrevoEmailSender>());
        var sent = await sender.SendAsync(toEmail, subject, body, ct);
        if (sent)
        {
            _failures?.RecordSuccess(MonitoredServices.Brevo.Key);
        }
        else
        {
            _failures?.RecordFailure(MonitoredServices.Brevo.Key, "send_failed");
        }

        return sent;
    }
}
