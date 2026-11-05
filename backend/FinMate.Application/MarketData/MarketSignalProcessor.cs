using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.MarketData;

public static class MarketEventText
{
    public static string Topic(MarketTopic topic) => topic switch
    {
        MarketTopic.Fuel => "fuel",
        MarketTopic.Electricity => "electricity",
        MarketTopic.Water => "water",
        MarketTopic.FoodPrice => "food_price",
        MarketTopic.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(topic), topic, null),
    };

    public static string Direction(MarketDirection direction) => direction.ToString().ToLowerInvariant();

    public static string Status(MarketEventStatus status) => status.ToString().ToLowerInvariant();

    public static MarketEventStatus? ParseStatus(string? text) => text switch
    {
        "pending" => MarketEventStatus.Pending,
        "approved" => MarketEventStatus.Approved,
        "rejected" => MarketEventStatus.Rejected,
        "expired" => MarketEventStatus.Expired,
        _ => null,
    };
}

/// <summary>
/// Cửa duy nhất để kết quả của AI được lưu. AI có thể tự nghĩ ra thông tin hoặc bị nội dung trang đánh lừa, nên đoạn trích phải có nguyên văn trong bài
/// và con số phần trăm phải nằm trong đoạn trích đó. Việc này không chứng minh được bài báo đúng, chỉ chứng minh thông tin thực sự đến từ bài.
/// </summary>
public static class MarketSignalValidator
{
    public const int MaxEvidenceLength = 300;

    /// <summary>Vừa cột <c>numeric(7,2)</c>. Trên mức này chắc chắn không phải mức đổi giá thật.</summary>
    private const decimal MaxPercent = 9_999m;

    private static readonly Regex PercentInText = new(@"(?<![\d.,])(\d+(?:[.,]\d+)?)\s*(?:%|phần trăm)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public sealed record Valid(MarketTopic Topic, MarketDirection Direction, MarketFactStatus FactStatus, decimal? ChangePercent, DateOnly? EffectiveDate, string Evidence);

    public static Valid? Validate(MarketAiSignal signal, string articleText)
    {
        var evidence = signal.Evidence?.Trim();
        if (string.IsNullOrEmpty(evidence) || evidence.Length > MaxEvidenceLength
            || ParseTopic(signal.Topic) is not { } topic
            || ParseDirection(signal.Direction) is not { } direction
            || ParseFact(signal.FactStatus) is not { } fact
            || !Normalize(articleText).Contains(Normalize(evidence), StringComparison.Ordinal))
        {
            return null;
        }

        if (signal.ChangePercent is { } percent && (Math.Abs(percent) > MaxPercent || !PercentAppearsIn(percent, evidence)))
        {
            return null;
        }

        var date = DateOnly.TryParseExact(signal.EffectiveDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : (DateOnly?)null;
        return new Valid(topic, direction, fact, signal.ChangePercent, date, evidence);
    }

    internal static string Normalize(string text)
        => string.Join(' ', text.Normalize(NormalizationForm.FormC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>So sánh theo giá trị, vì văn bản giá điện hay viết <c>4,80%</c> trong khi AI trả <c>4.8</c>. Số không dấu chấm phần nghìn nên dấu phẩy và chấm đều là phần thập phân.</summary>
    private static bool PercentAppearsIn(decimal percent, string evidence)
        => PercentInText.Matches(evidence.Normalize(NormalizationForm.FormC))
            .Any(match => decimal.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value == Math.Abs(percent));

    private static MarketTopic? ParseTopic(string? text) => text switch
    {
        "fuel" => MarketTopic.Fuel,
        "electricity" => MarketTopic.Electricity,
        "water" => MarketTopic.Water,
        "food_price" => MarketTopic.FoodPrice,
        "other" => MarketTopic.Other,
        _ => null,
    };

    private static MarketDirection? ParseDirection(string? text) => text switch
    {
        "up" => MarketDirection.Up,
        "down" => MarketDirection.Down,
        "stable" => MarketDirection.Stable,
        _ => null,
    };

    private static MarketFactStatus? ParseFact(string? text) => text switch
    {
        "actual" => MarketFactStatus.Actual,
        "forecast" => MarketFactStatus.Forecast,
        _ => null,
    };
}

public interface IMarketSignalProcessor
{
    /// <summary>Gửi một bài cho AI, kiểm tra kết quả và lưu. Khác <see cref="MarketAiStatus.Ok"/> thì bài vẫn chờ.</summary>
    Task<MarketAiStatus> ProcessAsync(MarketDocument document, string text, MarketDataSettings settings, CancellationToken ct = default);
    Task RecordFailureAsync(MarketDocument document, CancellationToken ct = default);
    Task ExpireOldEventsAsync(CancellationToken ct = default);
}

public sealed class MarketSignalProcessor : IMarketSignalProcessor
{
    public const int EventWindowDays = 7;
    public const int MaxAiFailures = 3;
    public const int MaxAiTextChars = 20_000;
    private const int MaxAiTitleChars = 500;

    private readonly IMarketDataRepository _repository;
    private readonly IMarketAiClient _ai;

    public MarketSignalProcessor(IMarketDataRepository repository, IMarketAiClient ai)
    {
        _repository = repository;
        _ai = ai;
    }

    public async Task<MarketAiStatus> ProcessAsync(MarketDocument document, string text, MarketDataSettings settings, CancellationToken ct = default)
    {
        var limited = text.Length <= MaxAiTextChars ? text : text[..MaxAiTextChars];
        var title = document.Title.Length <= MaxAiTitleChars ? document.Title : document.Title[..MaxAiTitleChars];
        var previousAttempt = document.AiAttemptedAt;
        document.AiAttemptedAt = DateTimeOffset.UtcNow;
        await _repository.SaveChangesAsync(ct);

        var result = await _ai.ExtractAsync(title, limited, document.PublishedAt, ct);
        if (result.Status == MarketAiStatus.NotConfigured)
        {
            // Không có lượt gọi nào thật, không được tính vào trần mỗi ngày.
            document.AiAttemptedAt = previousAttempt;
            await _repository.SaveChangesAsync(ct);
            return result.Status;
        }

        if (result.Status != MarketAiStatus.Ok)
        {
            await RecordFailureAsync(document, ct);
            return result.Status;
        }

        var now = DateTimeOffset.UtcNow;
        var valid = result.Signals.Select(s => MarketSignalValidator.Validate(s, limited)).OfType<MarketSignalValidator.Valid>().ToList();
        foreach (var signal in valid)
        {
            _repository.AddSignal(new MarketSignal
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                Topic = signal.Topic,
                Direction = signal.Direction,
                FactStatus = signal.FactStatus,
                ChangePercent = signal.ChangePercent,
                EffectiveDate = signal.EffectiveDate,
                Evidence = signal.Evidence,
                CreatedAt = now,
            });
        }

        document.Status = MarketDocumentStatus.Processed;
        await _repository.SaveChangesAsync(ct);

        foreach (var key in valid.Where(IsEventCandidate).Select(s => (s.Topic, s.Direction)).Distinct())
        {
            await RefreshEventAsync(key.Topic, key.Direction, settings, now, ct);
        }

        return MarketAiStatus.Ok;
    }

    public async Task RecordFailureAsync(MarketDocument document, CancellationToken ct = default)
    {
        _repository.DiscardUnsavedSignals();
        document.AiFailures++;
        if (document.AiFailures >= MaxAiFailures)
        {
            document.Status = MarketDocumentStatus.Failed;
            document.ErrorCode = "AI_FAILED";
        }

        await _repository.SaveChangesAsync(ct);
    }

    public Task ExpireOldEventsAsync(CancellationToken ct = default)
        => _repository.ExpireEventsAsync(DateTimeOffset.UtcNow.AddDays(-EventWindowDays), ct);

    /// <summary>Tin về xăng không thành sự kiện vì đã có giá thật ở <c>fuel_prices</c>. Dự báo không thành sự kiện. "Không đổi" thì không có gì để báo.</summary>
    private static bool IsEventCandidate(MarketSignalValidator.Valid signal)
        => signal.FactStatus == MarketFactStatus.Actual
            && signal.Direction != MarketDirection.Stable
            && signal.Topic is MarketTopic.Electricity or MarketTopic.Water or MarketTopic.FoodPrice;

    private async Task RefreshEventAsync(MarketTopic topic, MarketDirection direction, MarketDataSettings settings, DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddDays(-EventWindowDays);
        var sources = await _repository.ListActualSignalSourcesAsync(topic, direction, since, ct);
        var domains = sources.Select(s => MarketDomains.Of(s.SourceUrl)).Distinct().Count();
        if (domains < settings.MinEventDomains)
        {
            return;
        }

        var firstSeen = sources.Min(s => s.SeenAt);
        var existing = await _repository.GetLatestEventAsync(topic, direction, since, ct);
        if (existing is not null)
        {
            // Sự kiện đã bị từ chối vẫn được cập nhật, để tin đăng lại không bắt admin từ chối lần nữa.
            existing.LastSeenAt = now;
            existing.SourceCount = domains;
            await _repository.SaveChangesAsync(ct);
            return;
        }

        await _repository.TryAddEventAsync(
            new MarketEvent
            {
                Id = Guid.NewGuid(),
                Topic = topic,
                Direction = direction,
                FirstSeenAt = firstSeen,
                LastSeenAt = now,
                SourceCount = domains,
                Status = settings.AutoPublishNewsEvents ? MarketEventStatus.Approved : MarketEventStatus.Pending,
            },
            ct);
    }
}

public interface IMarketEventService
{
    Task<MarketEventPageDto> ListAsync(MarketEventStatus? status, int offset, int limit, CancellationToken ct = default);
    Task<MarketEventDto> ApproveAsync(Guid adminId, string? ip, Guid id, string? note, CancellationToken ct = default);
    Task<MarketEventDto> RejectAsync(Guid adminId, string? ip, Guid id, string? note, CancellationToken ct = default);
}

public sealed class MarketEventService : IMarketEventService
{
    private const int MaxPageSize = 100;
    private const int MaxNoteLength = 500;

    private readonly IMarketDataRepository _repository;
    private readonly IAuditLogService _audit;
    private readonly IMarketAlertTrigger _alertTrigger;

    public MarketEventService(IMarketDataRepository repository, IAuditLogService audit, IMarketAlertTrigger alertTrigger)
    {
        _alertTrigger = alertTrigger;
        _repository = repository;
        _audit = audit;
    }

    public async Task<MarketEventPageDto> ListAsync(MarketEventStatus? status, int offset, int limit, CancellationToken ct = default)
    {
        offset = Math.Max(offset, 0);
        limit = Math.Clamp(limit, 1, MaxPageSize);
        var (items, total) = await _repository.ListEventsAsync(status, offset, limit, ct);
        var dtos = new List<MarketEventDto>(items.Count);
        foreach (var item in items)
        {
            dtos.Add(await ToDtoAsync(item, ct));
        }

        return new MarketEventPageDto(dtos, total, offset, limit);
    }

    public Task<MarketEventDto> ApproveAsync(Guid adminId, string? ip, Guid id, string? note, CancellationToken ct = default)
        => ReviewAsync(adminId, ip, id, MarketEventStatus.Approved, AuditEvents.AdminMarketEventApproved, note, ct);

    public Task<MarketEventDto> RejectAsync(Guid adminId, string? ip, Guid id, string? note, CancellationToken ct = default)
        => ReviewAsync(adminId, ip, id, MarketEventStatus.Rejected, AuditEvents.AdminMarketEventRejected, note, ct);

    private async Task<MarketEventDto> ReviewAsync(Guid adminId, string? ip, Guid id, MarketEventStatus status, string auditEvent, string? note, CancellationToken ct)
    {
        var trimmed = note?.Trim();
        if (trimmed?.Length > MaxNoteLength)
        {
            throw new BusinessRuleException("MARKET_EVENT_NOTE_TOO_LONG", $"Ghi chú tối đa {MaxNoteLength} ký tự.");
        }

        if (!await _repository.TryReviewEventAsync(id, status, adminId, string.IsNullOrEmpty(trimmed) ? null : trimmed, DateTimeOffset.UtcNow, ct))
        {
            _ = await _repository.GetEventAsync(id, ct) ?? throw new NotFoundException("Sự kiện", id);
            throw new ConflictException("MARKET_EVENT_NOT_PENDING", "Sự kiện này đã được xử lý.");
        }

        await _audit.LogAsync(auditEvent, adminId, ip, new { EventId = id, Note = trimmed }, ct);
        if (status == MarketEventStatus.Approved)
        {
            _alertTrigger.Enqueue();
        }

        var reviewed = await _repository.GetEventAsync(id, ct) ?? throw new NotFoundException("Sự kiện", id);
        return await ToDtoAsync(reviewed, ct);
    }

    private async Task<MarketEventDto> ToDtoAsync(MarketEvent item, CancellationToken ct)
    {
        var signals = await _repository.ListEventSignalsAsync(item.Topic, item.Direction, item.FirstSeenAt.AddDays(-1), ct);
        return new MarketEventDto(
            item.Id,
            MarketEventText.Topic(item.Topic),
            MarketEventText.Direction(item.Direction),
            item.FirstSeenAt,
            item.LastSeenAt,
            item.SourceCount,
            MarketEventText.Status(item.Status),
            item.ReviewedAt,
            item.ReviewNote,
            signals.Select(s => new MarketEventSignalDto(s.Title, s.Url, s.Evidence, s.ChangePercent, s.EffectiveDate, s.PublishedAt)).ToList());
    }
}
