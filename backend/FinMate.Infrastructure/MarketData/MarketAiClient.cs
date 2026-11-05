using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.MarketData;

/// <summary>Không log nội dung bài, đoạn trích hay phản hồi: chỉ mã trạng thái và tên kiểu lỗi.</summary>
public sealed class MarketAiClient : IMarketAiClient
{
    private const string Route = "api/v1/market-data/extract";

    private static readonly MarketAiResult Failed = new(MarketAiStatus.Failed, []);

    private readonly HttpClient _http;
    private readonly ILogger<MarketAiClient> _logger;

    public MarketAiClient(HttpClient http, ILogger<MarketAiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<MarketAiResult> ExtractAsync(string title, string text, DateTimeOffset? publishedAt, IReadOnlyList<MarketAiTopic> topics, CancellationToken ct = default)
    {
        var request = new
        {
            title,
            text,
            published_at = publishedAt?.ToString("O", CultureInfo.InvariantCulture),
            topics = topics.Select(t => new { key = t.Key, description = t.Description }).ToList(),
        };
        try
        {
            using var response = await _http.PostAsJsonAsync(Route, request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Market extraction via AI Service failed: HTTP {Status}", (int)response.StatusCode);
                return Failed;
            }

            return Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            _logger.LogWarning("Market extraction via AI Service unavailable: {Reason}", ex.GetType().Name);
            return Failed;
        }
    }

    internal static MarketAiResult Parse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        switch (root.GetProperty("status").GetString())
        {
            case "not_configured":
                return new MarketAiResult(MarketAiStatus.NotConfigured, []);
            case "ok":
                var signals = root.GetProperty("signals").EnumerateArray().Select(s => new MarketAiSignal(
                    Text(s, "topic"),
                    Text(s, "direction"),
                    Text(s, "fact_status"),
                    s.TryGetProperty("change_percent", out var percent) && percent.ValueKind == JsonValueKind.Number ? percent.GetDecimal() : null,
                    Text(s, "effective_date"),
                    Text(s, "evidence"))).ToList();
                return new MarketAiResult(MarketAiStatus.Ok, signals);
            default:
                return Failed;
        }
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
