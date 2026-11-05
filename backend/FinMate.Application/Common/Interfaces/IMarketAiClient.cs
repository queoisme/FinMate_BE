namespace FinMate.Application.Common.Interfaces;

public enum MarketAiStatus
{
    Ok,

    /// <summary>ai-service chưa có key. Không phải lỗi: bài ở lại chờ.</summary>
    NotConfigured,
    Failed,
}

public sealed record MarketAiSignal(string? Topic, string? Direction, string? FactStatus, decimal? ChangePercent, string? EffectiveDate, string? Evidence);

public sealed record MarketAiTopic(string Key, string Description);

public sealed record MarketAiResult(MarketAiStatus Status, IReadOnlyList<MarketAiSignal> Signals);

/// <summary>Gọi <c>POST /api/v1/market-data/extract</c> của ai-service. Chỉ gửi nội dung trang công khai, không có dữ liệu người dùng.</summary>
public interface IMarketAiClient
{
    Task<MarketAiResult> ExtractAsync(string title, string text, DateTimeOffset? publishedAt, IReadOnlyList<MarketAiTopic> topics, CancellationToken ct = default);
}
