using System.Net.Http.Json;
using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace FinMate.Infrastructure.ExternalServices;

/// <summary>
/// Thử từng key của backend bằng lệnh gọi KHÔNG gửi gì thật và không ghi gì: Brevo hỏi thông tin tài khoản, Google đổi một code giả (phân biệt "sai secret" với "code giả"), Firebase xin access token.
/// Thông báo trả về là câu cố định, không bao giờ chứa key hay phản hồi nguyên văn của nhà cung cấp.
/// </summary>
public class BackendSecretTester : ISecretTester
{
    private readonly ISecretVault _vault;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _configuration;
    private readonly FcmApp _fcm;

    public BackendSecretTester(ISecretVault vault, IHttpClientFactory http, IConfiguration configuration, FcmApp fcm)
    {
        _vault = vault;
        _http = http;
        _configuration = configuration;
        _fcm = fcm;
    }

    public async Task<SecretTestResult> TestAsync(string name, CancellationToken ct = default)
    {
        var value = await _vault.GetAsync(name, ct);
        if (string.IsNullOrWhiteSpace(value) && name != BackendSecrets.FcmCredentialsJson)
        {
            return new SecretTestResult(false, "Chưa có key để thử.");
        }

        try
        {
            return name switch
            {
                BackendSecrets.BrevoApiKey => await TestBrevoAsync(value!, ct),
                BackendSecrets.GoogleClientSecret => await TestGoogleAsync(value!, ct),
                BackendSecrets.FcmCredentialsJson => await TestFirebaseAsync(ct),
                _ => new SecretTestResult(false, "Không có cách thử cho key này."),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new SecretTestResult(false, "Không gọi được nhà cung cấp (lỗi mạng hoặc quá thời gian). Thử lại sau.");
        }
    }

    private async Task<SecretTestResult> TestBrevoAsync(string key, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.brevo.com/v3/account");
        request.Headers.Add("api-key", key);
        using var response = await _http.CreateClient("brevo").SendAsync(request, ct);
        return response.IsSuccessStatusCode
            ? new SecretTestResult(true, "Key Brevo hoạt động.")
            : new SecretTestResult(false, (int)response.StatusCode is 401 or 403 ? "Brevo từ chối key này." : $"Brevo trả HTTP {(int)response.StatusCode}.");
    }

    private async Task<SecretTestResult> TestGoogleAsync(string secret, CancellationToken ct)
    {
        var clientId = _configuration["GOOGLE_CLIENT_ID"];
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return new SecretTestResult(false, "Chưa đặt GOOGLE_CLIENT_ID ở biến môi trường của backend.");
        }

        using var response = await _http.CreateClient("google-oauth").PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = "finmate-connection-test",
                ["client_id"] = clientId,
                ["client_secret"] = secret,
                ["redirect_uri"] = _configuration["GOOGLE_REDIRECT_URI"] ?? "http://localhost",
                ["grant_type"] = "authorization_code",
            }),
            ct);

        string? error = null;
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            error = document.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        }
        catch (JsonException)
        {
        }

        // code giả luôn bị từ chối: "invalid_grant" nghĩa là Google đã chấp nhận client id + secret rồi mới chê code; "invalid_client" là sai secret.
        return error switch
        {
            "invalid_grant" => new SecretTestResult(true, "Google chấp nhận Client ID và secret này."),
            "invalid_client" or "unauthorized_client" => new SecretTestResult(false, "Google từ chối Client ID hoặc secret."),
            _ => new SecretTestResult(false, $"Google trả kết quả không đoán được (HTTP {(int)response.StatusCode})."),
        };
    }

    private async Task<SecretTestResult> TestFirebaseAsync(CancellationToken ct)
    {
        try
        {
            return await _fcm.CanObtainTokenAsync(ct)
                ? new SecretTestResult(true, "Firebase cấp được token: credentials hợp lệ.")
                : new SecretTestResult(false, "Chưa có credentials Firebase hợp lệ để thử.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SecretTestResult(false, $"Firebase từ chối credentials ({ex.GetType().Name}).");
        }
    }
}
