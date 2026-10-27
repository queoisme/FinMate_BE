using System.Net.Http.Json;
using System.Text.Json;
using FinMate.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.ExternalServices;

/// <summary>
/// Gọi <c>POST /api/v1/finance-methods/recommend</c> của ai-service (docs/ai-finance-method-recommend-api.md). Khóa nội bộ và timeout nằm ở HttpClient
/// đã cấu hình ở DI.
///
/// KHÔNG ghi log signals, danh sách phương pháp, mã được chọn hay lý do — chỉ kết quả và mã trạng thái cố định, giống ràng buộc riêng tư của route.
/// Request không mang <c>user_id</c> hay mã băm của nó: ai-service không biết ai hỏi.
/// </summary>
public class MethodAiClient : IMethodAiClient
{
    private const string Route = "api/v1/finance-methods/recommend";

    private readonly HttpClient _http;
    private readonly ILogger<MethodAiClient> _logger;

    public MethodAiClient(HttpClient http, ILogger<MethodAiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<AiMethodPick?> RecommendAsync(
        IReadOnlyDictionary<string, int> signals,
        IReadOnlyList<AiMethodCandidate> methods,
        CancellationToken ct = default)
    {
        var request = new
        {
            signals = signals.Select(s => new { key = s.Key, weight = s.Value }).ToList(),
            methods = methods.Select(m => new { code = m.Code, name = m.Name, summary = m.Summary, role = m.Role }).ToList(),
        };

        try
        {
            using var response = await _http.PostAsJsonAsync(Route, request, ct);
            if (!response.IsSuccessStatusCode)
            {
                // 502 ai_invalid_output, 503 ai_unavailable/prompt_missing, 401, 422...: với backend đều có MỘT nghĩa — không có kết quả AI, dùng rule.
                _logger.LogWarning("Method recommendation via AI Service failed: HTTP {Status}", (int)response.StatusCode);
                return null;
            }

            var pick = Parse(await response.Content.ReadAsStringAsync(ct));
            if (pick is null)
            {
                _logger.LogWarning("Method recommendation via AI Service returned an unreadable response.");
            }

            return pick;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            _logger.LogWarning("Method recommendation via AI Service unavailable: {Reason}", ex.GetType().Name);
            return null;
        }
    }

    private static AiMethodPick? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("primary", out var primary)
            || Read(primary) is not { } primaryPick
            || !root.TryGetProperty("prompt_version", out var version)
            || version.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(version.GetString()))
        {
            return null;
        }

        (string Code, string Reason)? support = null;
        if (root.TryGetProperty("support", out var supportElement) && supportElement.ValueKind != JsonValueKind.Null)
        {
            support = Read(supportElement);
            if (support is null)
            {
                return null;   // `support` có mặt nhưng sai hình dạng: không đoán
            }
        }

        return new AiMethodPick(primaryPick.Code, primaryPick.Reason, support?.Code, support?.Reason, version.GetString()!);
    }

    private static (string Code, string Reason)? Read(JsonElement element)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
           && element.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String
            ? (code.GetString()!, reason.GetString()!)
            : null;
}
