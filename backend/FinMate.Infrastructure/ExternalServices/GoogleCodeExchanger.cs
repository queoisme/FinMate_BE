using System.Text.Json;
using System.Text.Json.Serialization;
using FinMate.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.ExternalServices;

/// <summary>
/// Đổi authorization code lấy id_token tại endpoint token của Google, bằng HttpClient trần.
///
/// Đây là chỗ DUY NHẤT trong hệ thống dùng tới client secret của Google — đường đăng nhập
/// native (<c>POST /auth/google</c>) chỉ xác minh token có sẵn nên không cần secret nào.
/// </summary>
public class GoogleCodeExchanger : IGoogleCodeExchanger
{
    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _redirectUri;
    private readonly ILogger<GoogleCodeExchanger> _logger;

    public GoogleCodeExchanger(
        HttpClient httpClient,
        string clientId,
        string clientSecret,
        string redirectUri,
        ILogger<GoogleCodeExchanger> logger)
    {
        _httpClient = httpClient;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _redirectUri = redirectUri;
        _logger = logger;
    }

    /// <summary>
    /// Tên trường phải khai TƯỜNG MINH: Google trả snake_case (<c>id_token</c>), mà bộ đọc
    /// JSON mặc định chỉ khớp được camelCase. Thiếu các attribute này thì phản hồi 200 hoàn
    /// toàn bình thường vẫn cho ra <c>IdToken = null</c> — hỏng im lặng, và log chỉ ghi
    /// "HTTP 200, không lỗi" nên nhìn vào không hiểu vì sao.
    /// </summary>
    internal sealed record TokenResponse(
        [property: JsonPropertyName("id_token")] string? IdToken,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);

    /// <summary>Bóc <c>id_token</c> ra khỏi phản hồi của Google; null nếu không có.</summary>
    internal static TokenResponse? ParseTokenResponse(string json)
        => JsonSerializer.Deserialize<TokenResponse>(json);

    public async Task<string?> ExchangeForIdTokenAsync(string code, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsync(
                "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["code"] = code,
                    ["client_id"] = _clientId,
                    ["client_secret"] = _clientSecret,

                    // Google đối chiếu lại redirect_uri ở bước này dù nó không điều hướng đi
                    // đâu nữa — sai một ký tự so với lúc /start là hỏng ngay tại đây.
                    ["redirect_uri"] = _redirectUri,
                    ["grant_type"] = "authorization_code",
                }),
                ct);

            var payload = ParseTokenResponse(await response.Content.ReadAsStringAsync(ct));

            if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(payload?.IdToken))
            {
                return payload.IdToken;
            }

            // Ghi error/error_description của Google, KHÔNG ghi nguyên body: body chứa cả
            // id_token và refresh token của Google. Cùng luật với BrevoEmailSender.
            _logger.LogWarning(
                "Google refused the authorization code: HTTP {StatusCode} {Error} {Description}",
                (int)response.StatusCode, payload?.Error, payload?.ErrorDescription);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google token exchange failed");
            return null;
        }
    }
}
