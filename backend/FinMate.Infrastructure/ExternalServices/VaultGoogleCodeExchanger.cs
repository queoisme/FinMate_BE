using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.ExternalServices;

/// <summary>
/// Đổi code Google bằng client secret ĐANG HIỆU LỰC tại lúc đổi (kho key trước, biến môi trường sau). Chưa có secret thì trả null như bản "tắt" trước đây, để lỗi rơi đúng vào chỗ kiểm tra cấu hình
/// của controller với thông báo rõ nguyên nhân.
/// </summary>
public class VaultGoogleCodeExchanger : IGoogleCodeExchanger
{
    private readonly ISecretVault _vault;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggers;

    public VaultGoogleCodeExchanger(ISecretVault vault, IHttpClientFactory http, IConfiguration configuration, ILoggerFactory loggers)
    {
        _vault = vault;
        _http = http;
        _configuration = configuration;
        _loggers = loggers;
    }

    public async Task<string?> ExchangeForIdTokenAsync(string code, CancellationToken ct = default)
    {
        var secret = await _vault.GetAsync(BackendSecrets.GoogleClientSecret, ct);
        var clientId = _configuration["GOOGLE_CLIENT_ID"];
        var redirectUri = _configuration["GOOGLE_REDIRECT_URI"];
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(redirectUri))
        {
            return null;
        }

        var exchanger = new GoogleCodeExchanger(_http.CreateClient("google-oauth"), clientId, secret, redirectUri, _loggers.CreateLogger<GoogleCodeExchanger>());
        return await exchanger.ExchangeForIdTokenAsync(code, ct);
    }
}
