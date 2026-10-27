using System.Net;
using System.Text;
using System.Text.Json;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Infrastructure.ExternalServices;

/// <summary>Chuyển tiếp API quản trị luật tới AI Service (khóa nội bộ nằm trong HttpClient đã cấu hình ở DI).</summary>
public class AiRulesAdminClient : IAiRulesAdminClient
{
    private const string Prefix = "api/v1/admin/";
    private const string ActorHeader = "X-Actor-Hash";

    private readonly HttpClient _http;

    public AiRulesAdminClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<JsonElement?> SendAsync(AiRulesRequest request, CancellationToken ct = default)
    {
        using var message = new HttpRequestMessage(request.Method, $"{Prefix}{request.Area}/{request.Path.TrimStart('/')}{BuildQuery(request.Query)}");
        if (request.ActorId is { } actor)
        {
            message.Headers.Add(ActorHeader, UserIdHasher.Hash(actor));
        }

        if (request.Body is { } body)
        {
            message.Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        string text;
        try
        {
            response = await _http.SendAsync(message, ct);
            text = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new AIServiceUnavailableException("AI Service không phản hồi.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                using var document = JsonDocument.Parse(text);
                return document.RootElement.Clone();
            }

            throw MapFailure(response.StatusCode, text);
        }
    }

    private static string BuildQuery(IReadOnlyDictionary<string, string?>? query)
    {
        var pairs = (query ?? new Dictionary<string, string?>())
            .Where(p => p.Value is not null)
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}")
            .ToList();
        return pairs.Count == 0 ? string.Empty : "?" + string.Join('&', pairs);
    }

    /// <summary>404/409/422 là câu trả lời nghiệp vụ của AI Service (giữ nguyên); mọi thứ khác (401, 5xx...) là AI Service hỏng hoặc cấu hình sai khóa.</summary>
    private static Exception MapFailure(HttpStatusCode status, string text)
    {
        if (status is not (HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity))
        {
            return new AIServiceUnavailableException("AI Service trả lỗi.");
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                return new AiRuleException(
                    (int)status,
                    error.TryGetProperty("code", out var code) ? code.GetString() ?? "AI_RULE_ERROR" : "AI_RULE_ERROR",
                    error.TryGetProperty("message", out var message) ? message.GetString() ?? "Yêu cầu không hợp lệ." : "Yêu cầu không hợp lệ.");
            }

            // Lỗi kiểm tra đầu vào của FastAPI: {"detail":[{"loc":[...],"msg":"..."}]}
            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.Array && detail.GetArrayLength() > 0
                && detail[0].TryGetProperty("msg", out var msg))
            {
                return new AiRuleException((int)status, "VALIDATION", msg.GetString() ?? "Dữ liệu không hợp lệ.");
            }
        }
        catch (JsonException)
        {
            // thân không phải JSON: rơi xuống thông điệp chung
        }

        return new AiRuleException((int)status, status == HttpStatusCode.NotFound ? "NOT_FOUND" : "AI_RULE_ERROR", "AI Service từ chối yêu cầu.");
    }
}
