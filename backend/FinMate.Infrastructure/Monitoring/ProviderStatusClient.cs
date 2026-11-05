using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.Monitoring;

public sealed record ProviderHealthBody(
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("consecutive_failures")] int ConsecutiveFailures,
    [property: JsonPropertyName("last_error_code")] string? LastErrorCode,
    [property: JsonPropertyName("last_success_at")] DateTimeOffset? LastSuccessAt = null);

public sealed record OcrStatusBody([property: JsonPropertyName("vision")] ProviderHealthBody? Vision);

public sealed record RasaStatusBody(
    [property: JsonPropertyName("configured")] bool Configured,
    [property: JsonPropertyName("available")] bool Available);

public sealed record AiStatusBody(
    [property: JsonPropertyName("gemini")] ProviderHealthBody? Gemini,
    [property: JsonPropertyName("gemini_backup")] ProviderHealthBody? GeminiBackup,
    [property: JsonPropertyName("rasa")] RasaStatusBody? Rasa);

/// <summary>Đọc trạng thái nhà cung cấp từ các dịch vụ Python. Null nghĩa là chưa biết: dịch vụ bản cũ chưa có API này, hoặc đang không gọi được, việc đó health probe đã theo dõi riêng.</summary>
public interface IProviderStatusClient
{
    Task<OcrStatusBody?> GetOcrStatusAsync(CancellationToken ct = default);
    Task<AiStatusBody?> GetAiStatusAsync(CancellationToken ct = default);
}

public sealed record ProviderStatusEndpoints(string? OcrUrl, string? OcrApiKey, string AiUrl, string AiApiKey);

public sealed class ProviderStatusClient : IProviderStatusClient
{
    private readonly IHttpClientFactory _http;
    private readonly ProviderStatusEndpoints _endpoints;
    private readonly ILogger<ProviderStatusClient> _logger;

    public ProviderStatusClient(IHttpClientFactory http, ProviderStatusEndpoints endpoints, ILogger<ProviderStatusClient> logger)
    {
        _http = http;
        _endpoints = endpoints;
        _logger = logger;
    }

    public Task<OcrStatusBody?> GetOcrStatusAsync(CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(_endpoints.OcrUrl)
            ? Task.FromResult<OcrStatusBody?>(null)
            : ReadAsync<OcrStatusBody>($"{_endpoints.OcrUrl.TrimEnd('/')}/internal/status", _endpoints.OcrApiKey, ct);

    public Task<AiStatusBody?> GetAiStatusAsync(CancellationToken ct = default)
        => ReadAsync<AiStatusBody>($"{_endpoints.AiUrl.TrimEnd('/')}/api/v1/internal/status", _endpoints.AiApiKey, ct);

    private async Task<T?> ReadAsync<T>(string url, string? apiKey, CancellationToken ct)
        where T : class
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _http.CreateClient(HttpServiceProbe.HttpClientName).SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Chưa đọc được trạng thái nhà cung cấp: HTTP {StatusCode}", (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<T>(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogInformation("Chưa đọc được trạng thái nhà cung cấp: {ExceptionType}", ex.GetType().Name);
            return null;
        }
    }
}
