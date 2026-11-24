using System.Security.Cryptography;
using System.Text;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Monitoring;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.MarketData;

public record MarketSourceDto(
    Guid Id,
    string Name,
    string Url,
    string Kind,
    bool AiAllowed,
    bool IsActive,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset? LastSuccessAt,
    int ConsecutiveFailures,
    string? LastErrorCode,
    DateTimeOffset CreatedAt,
    string TrustTier);

public record CreatedMarketSourceDto(MarketSourceDto Source, IReadOnlyList<string> Warnings);

public record BulkSourceResult(string Url, bool Created, MarketSourceDto? Source, IReadOnlyList<string> Warnings, string? ErrorCode, string? ErrorMessage);

public record MarketDocumentDto(Guid Id, Guid SourceId, string Url, string Title, DateTimeOffset? PublishedAt, int TextLength, string Status, string? ErrorCode, DateTimeOffset FetchedAt);

public record MarketDocumentPageDto(IReadOnlyList<MarketDocumentDto> Items, int Total, int Offset, int Limit);

public record FuelPriceDto(Guid Id, string FuelKey, long Region1PriceCents, long? Region2PriceCents, DateTimeOffset FirstSeenAt, string Origin, string? Note, IReadOnlyList<string>? SourceDomains);

public record FuelPricePageDto(IReadOnlyList<FuelPriceDto> Items, int Total, int Offset, int Limit);

public record FuelReadingDto(string FuelKey, long Region1PriceCents, long? Region2PriceCents);

public record GoldPriceDto(Guid Id, string GoldKey, long BuyCents, long SellCents, DateTimeOffset FirstSeenAt, string Origin, string? Note, IReadOnlyList<string>? SourceDomains);

public record GoldPricePageDto(IReadOnlyList<GoldPriceDto> Items, int Total, int Offset, int Limit);

public record GoldReadingDto(string GoldKey, long BuyCents, long SellCents);

public sealed record CorrectGoldPriceInput(string GoldKey, long BuyCents, long SellCents, string? Note);

public record ArticlePreviewDto(string Url, string Title, int TextLength, string Status);

public record MarketSourceTestDto(string Kind, bool AiAllowed, IReadOnlyList<FuelReadingDto> FuelPrices, string? FuelErrorCode, IReadOnlyList<ArticlePreviewDto> Articles, IReadOnlyList<string> Warnings, IReadOnlyList<GoldReadingDto>? GoldPrices = null);

public record MarketRefreshSummary(int SourcesChecked, int SourcesFailed, int NewDocuments, IReadOnlyList<string> ConfirmedFuels, IReadOnlyList<string> HeldBackFuels, int AiProcessed = 0, string? AiStatus = null, IReadOnlyList<string>? ConfirmedGold = null);

public sealed record UpdateMarketSourceInput(string? Name, MarketSourceKind? Kind, bool? IsActive);

public sealed record CorrectFuelPriceInput(string FuelKey, long Region1PriceCents, long? Region2PriceCents, string? Note);

public static class MarketErrorCodes
{
    public const string InvalidUrl = "MARKET_SOURCE_URL_INVALID";
    public const string AddressNotAllowed = "MARKET_SOURCE_ADDRESS_NOT_ALLOWED";
    public const string RobotsDisallowed = "MARKET_SOURCE_ROBOTS_DISALLOWED";
    public const string RobotsUnavailable = "MARKET_SOURCE_ROBOTS_UNAVAILABLE";
    public const string Refused = "MARKET_SOURCE_REFUSED";
    public const string NotFound = "MARKET_SOURCE_NOT_FOUND";
    public const string TooLarge = "MARKET_SOURCE_TOO_LARGE";
    public const string UnsupportedContent = "MARKET_SOURCE_UNSUPPORTED_CONTENT";
    public const string Unreachable = "MARKET_SOURCE_UNREACHABLE";
    public const string Duplicate = "MARKET_SOURCE_DUPLICATE";
    public const string NotTrusted = "MARKET_SOURCE_NOT_TRUSTED";
    public const string ReleaseNotFound = "FUEL_RELEASE_NOT_FOUND";
    public const string NoContent = "NO_CONTENT";
    public const string NoRelevantLinks = "NO_RELEVANT_LINKS";
    public const string BulkTooLarge = "MARKET_SOURCE_BULK_TOO_LARGE";
    public const string UnknownFuel = "FUEL_KEY_UNKNOWN";
    public const string NoteRequired = "FUEL_NOTE_REQUIRED";
    public const string NoChange = "FUEL_PRICE_UNCHANGED";
    public const string UnknownGold = "GOLD_KEY_UNKNOWN";
    public const string GoldPriceInvalid = "GOLD_PRICE_INVALID";
    public const string GoldNoChange = "GOLD_PRICE_UNCHANGED";
}

public static class MarketText
{
    public static string Kind(MarketSourceKind kind) => kind switch
    {
        MarketSourceKind.Feed => "feed",
        MarketSourceKind.PriceTable => "price_table",
        MarketSourceKind.Page => "page",
        MarketSourceKind.FuelRelease => "fuel_release",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static MarketSourceKind? ParseKind(string? text) => text switch
    {
        "feed" => MarketSourceKind.Feed,
        "price_table" => MarketSourceKind.PriceTable,
        "page" => MarketSourceKind.Page,
        "fuel_release" => MarketSourceKind.FuelRelease,
        _ => null,
    };

    public static string Status(MarketDocumentStatus status) => status switch
    {
        MarketDocumentStatus.PendingAi => "pending_ai",
        MarketDocumentStatus.Processed => "processed",
        MarketDocumentStatus.AiNotAllowed => "ai_not_allowed",
        MarketDocumentStatus.NoContent => "no_content",
        MarketDocumentStatus.Blocked => "blocked",
        MarketDocumentStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static MarketDocumentStatus? ParseStatus(string? text) => text switch
    {
        "pending_ai" => MarketDocumentStatus.PendingAi,
        "processed" => MarketDocumentStatus.Processed,
        "ai_not_allowed" => MarketDocumentStatus.AiNotAllowed,
        "no_content" => MarketDocumentStatus.NoContent,
        "blocked" => MarketDocumentStatus.Blocked,
        "failed" => MarketDocumentStatus.Failed,
        _ => null,
    };
}

public record MarketEventSignalDto(string Title, string Url, string Evidence, decimal? ChangePercent, DateOnly? EffectiveDate, DateTimeOffset? PublishedAt);

public record MarketEventDto(Guid Id, string Topic, string Direction, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt, int SourceCount, string Status, DateTimeOffset? ReviewedAt, string? ReviewNote, IReadOnlyList<MarketEventSignalDto> Signals);

public record MarketEventPageDto(IReadOnlyList<MarketEventDto> Items, int Total, int Offset, int Limit);

public interface IMarketDataService
{
    Task<IReadOnlyList<MarketSourceDto>> ListSourcesAsync(CancellationToken ct = default);
    Task<CreatedMarketSourceDto> CreateSourceAsync(Guid adminId, string? ip, string? url, string? name, CancellationToken ct = default);
    Task<IReadOnlyList<BulkSourceResult>> CreateSourcesAsync(Guid adminId, string? ip, IReadOnlyList<string> urls, CancellationToken ct = default);
    Task<MarketSourceDto> UpdateSourceAsync(Guid adminId, string? ip, Guid id, UpdateMarketSourceInput input, CancellationToken ct = default);
    Task<MarketSourceTestDto> TestSourceAsync(Guid id, CancellationToken ct = default);
    Task<MarketDocumentPageDto> ListDocumentsAsync(Guid? sourceId, MarketDocumentStatus? status, int offset, int limit, CancellationToken ct = default);
    Task<FuelPricePageDto> ListFuelPricesAsync(string? fuelKey, int offset, int limit, CancellationToken ct = default);
    Task<FuelPriceDto> CorrectFuelPriceAsync(Guid adminId, string? ip, CorrectFuelPriceInput input, CancellationToken ct = default);
    Task<GoldPricePageDto> ListGoldPricesAsync(string? goldKey, int offset, int limit, CancellationToken ct = default);
    Task<GoldPriceDto> CorrectGoldPriceAsync(Guid adminId, string? ip, CorrectGoldPriceInput input, CancellationToken ct = default);
    Task<MarketRefreshSummary> RefreshAsync(CancellationToken ct = default);
    Task<int> PurgeOldDocumentsAsync(CancellationToken ct = default);
}

/// <summary>
/// Đọc các website admin đưa vào. Bảng giá đọc bằng luật ra số chính xác; tin bài chỉ lấy tới mức biết bài có đọc được hay không, và không lưu toàn văn.
/// Trang từ chối thì dừng: không đổi User-Agent, không tìm cách vượt.
/// </summary>
public sealed class MarketDataService : IMarketDataService
{
    public const int MaxBulkUrls = 50;
    private const int MaxPageSize = 200;
    private const int MaxUrlLength = 2000;
    private const int MaxNameLength = 200;
    private const int MaxTitleLength = 500;
    private const int MaxNoteLength = 500;
    private const int MinNoteLength = 3;
    private const int DocumentRetentionDays = 180;
    private const int PreviewArticles = 3;

    private readonly IMarketDataRepository _repository;
    private readonly IWebPageFetcher _fetcher;
    private readonly ISystemConfigProvider _config;
    private readonly IAuditLogService _audit;
    private readonly IMarketSignalProcessor _signals;
    private readonly IMarketAlertTrigger _alertTrigger;
    private readonly IProviderFailureTracker _tracker;
    private readonly ILogger<MarketDataService> _logger;
    private readonly Dictionary<string, string> _freshTexts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RobotsLookup> _robots = new(StringComparer.OrdinalIgnoreCase);

    public MarketDataService(IMarketDataRepository repository, IWebPageFetcher fetcher, ISystemConfigProvider config, IAuditLogService audit, IMarketSignalProcessor signals, IMarketAlertTrigger alertTrigger, IProviderFailureTracker tracker, ILogger<MarketDataService> logger)
    {
        _alertTrigger = alertTrigger;
        _tracker = tracker;
        _signals = signals;
        _repository = repository;
        _fetcher = fetcher;
        _config = config;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MarketSourceDto>> ListSourcesAsync(CancellationToken ct = default)
    {
        var settings = (await _config.GetAsync(ct)).Document.MarketData;
        return (await _repository.ListSourcesAsync(ct)).Select(s => ToDto(s, settings)).ToList();
    }

    public async Task<CreatedMarketSourceDto> CreateSourceAsync(Guid adminId, string? ip, string? url, string? name, CancellationToken ct = default)
    {
        var uri = ParseUrl(url);
        var settings = (await _config.GetAsync(ct)).Document.MarketData;
        if (!MarketTrust.IsTrusted(uri.AbsoluteUri, settings))
        {
            throw new BusinessRuleException(MarketErrorCodes.NotTrusted, Explain(MarketErrorCodes.NotTrusted));
        }

        var inspection = await InspectAsync(uri, settings, ct);
        if (inspection.ErrorCode is { } error)
        {
            throw new BusinessRuleException(error, Explain(error));
        }

        var now = DateTimeOffset.UtcNow;
        var trimmedName = name?.Trim();
        var source = new MarketSource
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrEmpty(trimmedName) ? uri.Host : Limit(trimmedName, MaxNameLength),
            Url = uri.AbsoluteUri,
            Kind = inspection.Kind,
            AiAllowed = inspection.AiAllowed,
            IsActive = true,
            CreatedByUserId = adminId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (!await _repository.TryAddSourceAsync(source, ct))
        {
            throw new ConflictException(MarketErrorCodes.Duplicate, "Link này đã có trong danh sách nguồn.");
        }

        await _audit.LogAsync(AuditEvents.AdminMarketSourceCreated, adminId, ip, new { source.Id, source.Url, Kind = MarketText.Kind(source.Kind) }, ct);
        return new CreatedMarketSourceDto(ToDto(source, settings), inspection.Warnings);
    }

    public async Task<IReadOnlyList<BulkSourceResult>> CreateSourcesAsync(Guid adminId, string? ip, IReadOnlyList<string> urls, CancellationToken ct = default)
    {
        if (urls.Count == 0 || urls.Count > MaxBulkUrls)
        {
            throw new BusinessRuleException(MarketErrorCodes.BulkTooLarge, $"Mỗi lần gửi từ 1 đến {MaxBulkUrls} link.");
        }

        var results = new List<BulkSourceResult>(urls.Count);
        foreach (var url in urls)
        {
            try
            {
                var created = await CreateSourceAsync(adminId, ip, url, null, ct);
                results.Add(new BulkSourceResult(url, true, created.Source, created.Warnings, null, null));
            }
            catch (Exception ex) when (ex is BusinessRuleException or ConflictException)
            {
                var finmate = (FinMateException)ex;
                results.Add(new BulkSourceResult(url, false, null, [], finmate.ErrorCode, finmate.Message));
            }
        }

        return results;
    }

    public async Task<MarketSourceDto> UpdateSourceAsync(Guid adminId, string? ip, Guid id, UpdateMarketSourceInput input, CancellationToken ct = default)
    {
        var source = await _repository.GetSourceAsync(id, ct) ?? throw new NotFoundException("Nguồn", id);
        var name = input.Name?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            source.Name = Limit(name, MaxNameLength);
        }

        var settings = (await _config.GetAsync(ct)).Document.MarketData;
        if (input.Kind is { } kind)
        {
            if (kind == MarketSourceKind.FuelRelease && MarketTrust.TierOf(source.Url, settings) != MarketTrustedDomain.Official)
            {
                throw new BusinessRuleException(MarketErrorCodes.NotTrusted, "Chỉ nguồn hạng official mới được đọc thông cáo giá xăng.");
            }

            source.Kind = kind;
        }

        if (input.IsActive is { } active)
        {
            source.IsActive = active;
        }

        source.UpdatedAt = DateTimeOffset.UtcNow;
        await _repository.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.AdminMarketSourceUpdated, adminId, ip, new { source.Id, Kind = MarketText.Kind(source.Kind), source.IsActive }, ct);
        return ToDto(source, settings);
    }

    public async Task<MarketSourceTestDto> TestSourceAsync(Guid id, CancellationToken ct = default)
    {
        var source = await _repository.GetSourceAsync(id, ct) ?? throw new NotFoundException("Nguồn", id);
        var settings = (await _config.GetAsync(ct)).Document.MarketData;
        var uri = new Uri(source.Url);
        var (rules, robotsError) = await RobotsForAsync(uri, ct);
        if (rules is null)
        {
            return new MarketSourceTestDto(MarketText.Kind(source.Kind), source.AiAllowed, [], null, [], [robotsError!]);
        }

        if (!rules.IsAllowed(uri.PathAndQuery))
        {
            return new MarketSourceTestDto(MarketText.Kind(source.Kind), rules.AiAllowed, [], null, [], [MarketErrorCodes.RobotsDisallowed]);
        }

        var fetched = await _fetcher.FetchAsync(uri, ct);
        if (fetched.Outcome != WebFetchOutcome.Ok)
        {
            return new MarketSourceTestDto(MarketText.Kind(source.Kind), rules.AiAllowed, [], null, [], [ErrorCodeFor(fetched.Outcome)]);
        }

        var content = fetched.Content ?? string.Empty;
        if (source.Kind == MarketSourceKind.PriceTable)
        {
            var text = HtmlTextExtractor.PageText(content);
            var readings = FuelTableParser.Parse(text);
            var error = FuelReadingValidator.ValidateTable(readings, settings);
            var gold = GoldTableParser.Parse(text, settings.Gold);
            return new MarketSourceTestDto(MarketText.Kind(source.Kind), rules.AiAllowed, readings.Select(ToDto).ToList(), error, [], [], gold.Select(ToDto).ToList());
        }

        if (source.Kind == MarketSourceKind.FuelRelease)
        {
            var release = await ReadReleaseAsync(content, fetched.FinalUri ?? uri, settings, ct);
            return new MarketSourceTestDto(MarketText.Kind(source.Kind), rules.AiAllowed, release.Readings.Select(ToDto).ToList(), release.ErrorCode, [], []);
        }

        var candidates = Candidates(source, content, fetched.FinalUri ?? uri, settings).ToList();
        var previews = new List<ArticlePreviewDto>();
        foreach (var candidate in candidates.Take(PreviewArticles))
        {
            var read = await ReadArticleAsync(candidate, ct);
            previews.Add(new ArticlePreviewDto(candidate.Url, candidate.Title, read.TextLength, MarketText.Status(read.Status)));
        }

        return new MarketSourceTestDto(MarketText.Kind(source.Kind), rules.AiAllowed, [], null, previews, candidates.Count == 0 ? [MarketErrorCodes.NoRelevantLinks] : []);
    }

    public async Task<MarketDocumentPageDto> ListDocumentsAsync(Guid? sourceId, MarketDocumentStatus? status, int offset, int limit, CancellationToken ct = default)
    {
        offset = Math.Max(offset, 0);
        limit = Math.Clamp(limit, 1, MaxPageSize);
        var (items, total) = await _repository.ListDocumentsAsync(sourceId, status, offset, limit, ct);
        return new MarketDocumentPageDto(
            items.Select(d => new MarketDocumentDto(d.Id, d.SourceId, d.Url, d.Title, d.PublishedAt, d.TextLength, MarketText.Status(d.Status), d.ErrorCode, d.FetchedAt)).ToList(),
            total,
            offset,
            limit);
    }

    public async Task<FuelPricePageDto> ListFuelPricesAsync(string? fuelKey, int offset, int limit, CancellationToken ct = default)
    {
        offset = Math.Max(offset, 0);
        limit = Math.Clamp(limit, 1, MaxPageSize);
        var (items, total) = await _repository.ListFuelPricesAsync(fuelKey, offset, limit, ct);
        return new FuelPricePageDto(items.Select(ToDto).ToList(), total, offset, limit);
    }

    public async Task<FuelPriceDto> CorrectFuelPriceAsync(Guid adminId, string? ip, CorrectFuelPriceInput input, CancellationToken ct = default)
    {
        if (!FuelKeys.All.Contains(input.FuelKey))
        {
            throw new BusinessRuleException(MarketErrorCodes.UnknownFuel, "Loại xăng dầu không hợp lệ.");
        }

        var note = input.Note?.Trim();
        if (note is null || note.Length < MinNoteLength || note.Length > MaxNoteLength)
        {
            throw new BusinessRuleException(MarketErrorCodes.NoteRequired, $"Ghi chú phải từ {MinNoteLength} đến {MaxNoteLength} ký tự.");
        }

        var settings = (await _config.GetAsync(ct)).Document.MarketData;
        var reading = new FuelReading(input.FuelKey, input.Region1PriceCents, input.Region2PriceCents);
        if (FuelReadingValidator.ValidatePrice(reading, settings) is { } error)
        {
            throw new BusinessRuleException(error, "Giá không hợp lệ: ngoài khoảng cho phép, hoặc giá vùng 2 thấp hơn vùng 1.");
        }

        var latest = (await _repository.ListLatestFuelPricesAsync(ct)).FirstOrDefault(p => p.FuelKey == input.FuelKey);
        if (latest is not null)
        {
            if (latest.Region1PriceCents == input.Region1PriceCents && latest.Region2PriceCents == input.Region2PriceCents)
            {
                throw new BusinessRuleException(MarketErrorCodes.NoChange, "Giá này giống giá đang lưu.");
            }

            if (!FuelReadingValidator.ChangeIsPlausible(latest.Region1PriceCents, input.Region1PriceCents, settings))
            {
                throw new BusinessRuleException(FuelErrorCodes.ChangeTooLarge, $"Giá đổi hơn {settings.MaxChangePercent}% so với giá đang lưu.");
            }
        }

        var price = new FuelPrice
        {
            Id = Guid.NewGuid(),
            FuelKey = input.FuelKey,
            Region1PriceCents = input.Region1PriceCents,
            Region2PriceCents = input.Region2PriceCents,
            FirstSeenAt = DateTimeOffset.UtcNow,
            Origin = FuelPriceOrigin.Manual,
            CreatedByUserId = adminId,
            Note = note,
        };
        _repository.AddFuelPrice(price);
        await _repository.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.AdminFuelPriceCorrected, adminId, ip, new { price.Id, price.FuelKey, price.Region1PriceCents, price.Region2PriceCents, Note = note }, ct);
        _alertTrigger.Enqueue();
        return ToDto(price);
    }

    public async Task<GoldPricePageDto> ListGoldPricesAsync(string? goldKey, int offset, int limit, CancellationToken ct = default)
    {
        offset = Math.Max(offset, 0);
        limit = Math.Clamp(limit, 1, MaxPageSize);
        var (items, total) = await _repository.ListGoldPricesAsync(goldKey, offset, limit, ct);
        return new GoldPricePageDto(items.Select(ToDto).ToList(), total, offset, limit);
    }

    public async Task<GoldPriceDto> CorrectGoldPriceAsync(Guid adminId, string? ip, CorrectGoldPriceInput input, CancellationToken ct = default)
    {
        var gold = (await _config.GetAsync(ct)).Document.MarketData.Gold;
        if (gold.Rows.All(r => r.Key != input.GoldKey))
        {
            throw new BusinessRuleException(MarketErrorCodes.UnknownGold, "Loại vàng không hợp lệ.");
        }

        var note = input.Note?.Trim();
        if (note is null || note.Length < MinNoteLength || note.Length > MaxNoteLength)
        {
            throw new BusinessRuleException(MarketErrorCodes.NoteRequired, $"Ghi chú phải từ {MinNoteLength} đến {MaxNoteLength} ký tự.");
        }

        if (!GoldTableParser.IsValid(new GoldReading(input.GoldKey, input.BuyCents, input.SellCents), gold))
        {
            throw new BusinessRuleException(MarketErrorCodes.GoldPriceInvalid, "Giá không hợp lệ: ngoài khoảng cho phép, giá mua cao hơn giá bán, hoặc chênh mua bán quá lớn.");
        }

        var latest = (await _repository.ListLatestGoldPricesAsync(ct)).FirstOrDefault(p => p.GoldKey == input.GoldKey);
        if (latest is not null)
        {
            if (latest.BuyCents == input.BuyCents && latest.SellCents == input.SellCents)
            {
                throw new BusinessRuleException(MarketErrorCodes.GoldNoChange, "Giá này giống giá đang lưu.");
            }

            if (Math.Abs(input.SellCents - latest.SellCents) * 100 > latest.SellCents * gold.MaxChangePercent)
            {
                throw new BusinessRuleException(FuelErrorCodes.ChangeTooLarge, $"Giá đổi hơn {gold.MaxChangePercent}% so với giá đang lưu.");
            }
        }

        var price = new GoldPrice
        {
            Id = Guid.NewGuid(),
            GoldKey = input.GoldKey,
            BuyCents = input.BuyCents,
            SellCents = input.SellCents,
            FirstSeenAt = DateTimeOffset.UtcNow,
            Origin = FuelPriceOrigin.Manual,
            CreatedByUserId = adminId,
            Note = note,
        };
        _repository.AddGoldPrice(price);
        await _repository.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.AdminGoldPriceCorrected, adminId, ip, new { price.Id, price.GoldKey, price.BuyCents, price.SellCents, Note = note }, ct);
        _alertTrigger.Enqueue();
        return ToDto(price);
    }

    public async Task<MarketRefreshSummary> RefreshAsync(CancellationToken ct = default)
    {
        var settings = (await _config.GetAsync(ct)).Document.MarketData;
        var sources = await _repository.ListActiveSourcesAsync(ct);
        var tables = new List<SourceFuelTable>();
        var goldTables = new List<SourceFuelTable>();
        var failed = 0;
        var newDocuments = 0;

        foreach (var source in sources)
        {
            var now = DateTimeOffset.UtcNow;
            source.LastCheckedAt = now;
            if (!MarketTrust.IsTrusted(source.Url, settings))
            {
                // Tên miền đã bị gỡ khỏi danh sách uy tín: không đọc, và không tính là lỗi của trang.
                source.LastErrorCode = MarketErrorCodes.NotTrusted;
                await _repository.SaveChangesAsync(ct);
                continue;
            }

            try
            {
                var outcome = await ReadSourceAsync(source, settings, ct);
                if (outcome.ErrorCode is null)
                {
                    source.LastSuccessAt = now;
                    source.ConsecutiveFailures = 0;
                    source.LastErrorCode = null;
                    newDocuments += outcome.NewDocuments;
                    if (outcome.Readings is not null)
                    {
                        var domain = MarketDomains.Of(source.Url);
                        var official = MarketTrust.TierOf(source.Url, settings) == MarketTrustedDomain.Official;
                        tables.Add(new SourceFuelTable(domain, outcome.Readings, official, MarketTrust.OwnerOf(domain, settings)));
                    }

                    if (outcome.Gold is { Count: > 0 })
                    {
                        var domain = MarketDomains.Of(source.Url);
                        var official = MarketTrust.TierOf(source.Url, settings) == MarketTrustedDomain.Official;
                        goldTables.Add(new SourceFuelTable(domain, outcome.Gold.Select(AsFuelShape).ToList(), official, MarketTrust.OwnerOf(domain, settings)));
                    }
                }
                else
                {
                    failed++;
                    source.ConsecutiveFailures++;
                    source.LastErrorCode = outcome.ErrorCode;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Lỗi của một nguồn không được chặn các nguồn còn lại. Không ghi message vì có thể chứa nội dung trang.
                failed++;
                source.ConsecutiveFailures++;
                source.LastErrorCode = "INTERNAL_ERROR";
                _logger.LogError("Market source {SourceId} failed: {ErrorType}", source.Id, ex.GetType().Name);
            }

            await _repository.SaveChangesAsync(ct);
        }

        var confirmation = await ConfirmPricesAsync(tables, settings, ct);
        var confirmedGold = await ConfirmGoldAsync(goldTables, settings, ct);
        RecordFuelHealth(sources.Count(s => s.Kind is MarketSourceKind.PriceTable or MarketSourceKind.FuelRelease && MarketTrust.IsTrusted(s.Url, settings)), confirmation);
        var (aiProcessed, aiStatus) = await ReadPendingWithAiAsync(settings, ct);
        await _signals.ExpireOldEventsAsync(ct);
        return new MarketRefreshSummary(sources.Count, failed, newDocuments, confirmation.Confirmed.Select(c => c.FuelKey).ToList(), confirmation.HeldBack, aiProcessed, aiStatus, confirmedGold);
    }

    /// <summary>Một nguồn lỗi riêng lẻ chỉ ghi vào nguồn đó. Sự cố chỉ mở khi nhiều lượt liền không xác nhận được giá, vì lúc đó cảnh báo xăng đã ngừng hoạt động.</summary>
    private void RecordFuelHealth(int priceTableSources, FuelConfirmation confirmation)
    {
        var provider = MonitoredServices.MarketData.Key;
        if (priceTableSources == 0)
        {
            _tracker.RecordNotConfigured(provider);
        }
        else if (confirmation.HeldBack.Count > 0)
        {
            _tracker.RecordFailure(provider, FuelErrorCodes.ChangeTooLarge);
        }
        else if (confirmation.AgreedFuels == 0)
        {
            _tracker.RecordFailure(provider, "NO_AGREEMENT");
        }
        else
        {
            _tracker.RecordSuccess(provider);
        }
    }

    public Task<int> PurgeOldDocumentsAsync(CancellationToken ct = default)
        => _repository.DeleteDocumentsBeforeAsync(DateTimeOffset.UtcNow.AddDays(-DocumentRetentionDays), ct);

    /// <summary>Đọc bài đang chờ, gồm cả bài của lượt trước khi AI chưa sẵn sàng. Không có key hoặc AI lỗi thì dừng, bài ở lại chờ lượt sau.</summary>
    private async Task<(int Processed, string? Status)> ReadPendingWithAiAsync(MarketDataSettings settings, CancellationToken ct)
    {
        await _repository.MarkPendingDocumentsNotAllowedAsync(ct);
        var used = await _repository.CountAiAttemptsSinceAsync(VietnamTime.DayRange(VietnamTime.Today()).Start, ct);
        var remaining = settings.MaxAiDocumentsPerDay - used;
        if (remaining <= 0)
        {
            return (0, "daily_limit");
        }

        var processed = 0;
        foreach (var document in await _repository.ListDocumentsForAiAsync(remaining, ct))
        {
            var text = await TextForAsync(document, ct);
            if (text is null)
            {
                continue;
            }

            MarketAiStatus outcome;
            try
            {
                outcome = await _signals.ProcessAsync(document, text, settings, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Một bài lỗi không được chặn các bài khác và phần gửi cảnh báo phía sau.
                _logger.LogError("Market document {DocumentId} failed: {ErrorType}", document.Id, ex.GetType().Name);
                await _signals.RecordFailureAsync(document, ct);
                continue;
            }

            if (outcome != MarketAiStatus.Ok)
            {
                return (processed, outcome == MarketAiStatus.NotConfigured ? "not_configured" : "failed");
            }

            processed++;
        }

        return (processed, null);
    }

    /// <summary>Bài vừa đọc thì dùng lại text trong bộ nhớ. Bài của lượt trước phải tải lại vì toàn văn không được lưu. Null nếu lần này chưa có text.</summary>
    private async Task<string?> TextForAsync(MarketDocument document, CancellationToken ct)
    {
        if (_freshTexts.TryGetValue(document.Url, out var fresh))
        {
            return fresh;
        }

        var read = await ReadArticleAsync(new ArticleCandidate(document.Url, document.Title, document.PublishedAt), ct);
        if (read.IsTemporary)
        {
            return null;
        }

        if (read.Status != MarketDocumentStatus.PendingAi)
        {
            document.Status = read.Status;
            document.ErrorCode = read.ErrorCode;
            await _repository.SaveChangesAsync(ct);
            return null;
        }

        return _freshTexts[document.Url];
    }

    private async Task<FuelConfirmation> ConfirmPricesAsync(IReadOnlyList<SourceFuelTable> tables, MarketDataSettings settings, CancellationToken ct)
    {
        var latestRows = await _repository.ListLatestFuelPricesAsync(ct);
        var latest = latestRows.ToDictionary(p => p.FuelKey, p => new FuelReading(p.FuelKey, p.Region1PriceCents, p.Region2PriceCents));

        // Admin vừa sửa vì nguồn sai: nguồn còn báo đúng giá cũ thì không được ghi đè, nguồn đổi sang giá khác mới là tin mới.
        var heldStale = new Dictionary<string, FuelReading>();
        foreach (var manual in latestRows.Where(p => p.Origin == FuelPriceOrigin.Manual))
        {
            if (await _repository.GetLatestSourceFuelPriceAsync(manual.FuelKey, ct) is { } source)
            {
                heldStale[manual.FuelKey] = new FuelReading(source.FuelKey, source.Region1PriceCents, source.Region2PriceCents);
            }
        }

        var confirmation = FuelPriceConfirmer.Confirm(tables, latest, settings, heldStale);
        var now = DateTimeOffset.UtcNow;
        foreach (var reading in confirmation.Confirmed)
        {
            _repository.AddFuelPrice(new FuelPrice
            {
                Id = Guid.NewGuid(),
                FuelKey = reading.FuelKey,
                Region1PriceCents = reading.Region1PriceCents,
                Region2PriceCents = reading.Region2PriceCents,
                FirstSeenAt = now,
                Origin = FuelPriceOrigin.Sources,
                SourceDomains = confirmation.Domains?.GetValueOrDefault(reading.FuelKey)?.ToArray(),
            });
        }

        await _repository.SaveChangesAsync(ct);
        if (confirmation.HeldBack.Count > 0)
        {
            _logger.LogWarning("Fuel prices held back as implausible: {FuelKeys}", string.Join(",", confirmation.HeldBack));
        }

        return confirmation;
    }

    /// <summary>
    /// Giá vàng dùng chung luật xác nhận với giá xăng (<see cref="FuelPriceConfirmer"/>): mua vào ở chỗ vùng 1, bán ra ở chỗ vùng 2, và trần thay đổi của vàng.
    /// Trần thay đổi so trên giá mua vào.
    /// </summary>
    private async Task<IReadOnlyList<string>> ConfirmGoldAsync(IReadOnlyList<SourceFuelTable> tables, MarketDataSettings settings, CancellationToken ct)
    {
        if (tables.Count == 0)
        {
            return [];
        }

        var latestRows = await _repository.ListLatestGoldPricesAsync(ct);
        var latest = latestRows.ToDictionary(p => p.GoldKey, p => new FuelReading(p.GoldKey, p.BuyCents, p.SellCents));
        var heldStale = new Dictionary<string, FuelReading>();
        foreach (var manual in latestRows.Where(p => p.Origin == FuelPriceOrigin.Manual))
        {
            if (await _repository.GetLatestSourceGoldPriceAsync(manual.GoldKey, ct) is { } source)
            {
                heldStale[manual.GoldKey] = new FuelReading(source.GoldKey, source.BuyCents, source.SellCents);
            }
        }

        var confirmation = FuelPriceConfirmer.Confirm(tables, latest, settings with { MaxChangePercent = settings.Gold.MaxChangePercent }, heldStale);
        var now = DateTimeOffset.UtcNow;
        foreach (var reading in confirmation.Confirmed)
        {
            _repository.AddGoldPrice(new GoldPrice
            {
                Id = Guid.NewGuid(),
                GoldKey = reading.FuelKey,
                BuyCents = reading.Region1PriceCents,
                SellCents = reading.Region2PriceCents!.Value,
                FirstSeenAt = now,
                Origin = FuelPriceOrigin.Sources,
                SourceDomains = confirmation.Domains?.GetValueOrDefault(reading.FuelKey)?.ToArray(),
            });
        }

        await _repository.SaveChangesAsync(ct);
        if (confirmation.HeldBack.Count > 0)
        {
            _logger.LogWarning("Gold prices held back as implausible: {GoldKeys}", string.Join(",", confirmation.HeldBack));
        }

        return confirmation.Confirmed.Select(r => r.FuelKey).ToList();
    }

    private static FuelReading AsFuelShape(GoldReading gold) => new(gold.GoldKey, gold.BuyCents, gold.SellCents);

    private sealed record SourceOutcome(string? ErrorCode, int NewDocuments = 0, IReadOnlyList<FuelReading>? Readings = null, IReadOnlyList<GoldReading>? Gold = null);

    private async Task<SourceOutcome> ReadSourceAsync(MarketSource source, MarketDataSettings settings, CancellationToken ct)
    {
        var uri = new Uri(source.Url);
        var (rules, robotsError) = await RobotsForAsync(uri, ct);
        if (rules is null)
        {
            return new SourceOutcome(robotsError);
        }

        source.AiAllowed = rules.AiAllowed;
        if (!rules.IsAllowed(uri.PathAndQuery))
        {
            return new SourceOutcome(MarketErrorCodes.RobotsDisallowed);
        }

        var fetched = await _fetcher.FetchAsync(uri, ct);
        if (fetched.Outcome != WebFetchOutcome.Ok)
        {
            return new SourceOutcome(ErrorCodeFor(fetched.Outcome));
        }

        var content = fetched.Content ?? string.Empty;
        if (source.Kind == MarketSourceKind.PriceTable)
        {
            // Luôn đọc lại dù trang không đổi: việc so khớp cần số của mọi nguồn trong cùng một lượt.
            // Một trang có thể chỉ có giá xăng, chỉ có giá vàng, hoặc cả hai.
            var text = HtmlTextExtractor.PageText(content);
            var readings = FuelTableParser.Parse(text);
            var error = FuelReadingValidator.ValidateTable(readings, settings);
            var gold = GoldTableParser.Parse(text, settings.Gold);
            if (error is not null && gold.Count == 0)
            {
                return new SourceOutcome(error);
            }

            return new SourceOutcome(null, 0, error is null ? readings : null, gold);
        }

        if (source.Kind == MarketSourceKind.FuelRelease)
        {
            var release = await ReadReleaseAsync(content, fetched.FinalUri ?? uri, settings, ct);
            return release.ErrorCode is null ? new SourceOutcome(null, 0, release.Readings) : new SourceOutcome(release.ErrorCode);
        }

        var hash = Hash(content);
        if (hash == source.LastContentHash)
        {
            return new SourceOutcome(null);
        }

        var candidates = Candidates(source, content, fetched.FinalUri ?? uri, settings).ToList();
        var known = await _repository.FindExistingDocumentUrlsAsync(candidates.Select(c => c.Url).ToList(), ct);
        var added = 0;
        var unread = candidates.Where(c => !known.Contains(c.Url)).ToList();

        // Còn bài vượt trần thì cũng phải đọc tiếp ở lượt sau, dù trang không đổi.
        var retryLater = unread.Count > settings.MaxDocumentsPerRun;
        foreach (var candidate in unread.Take(settings.MaxDocumentsPerRun))
        {
            var read = await ReadArticleAsync(candidate, ct);
            if (read.IsTemporary)
            {
                retryLater = true;
                continue;
            }

            var document = new MarketDocument
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                Url = candidate.Url,
                Title = Limit(candidate.Title, MaxTitleLength),
                PublishedAt = candidate.PublishedAt ?? read.PublishedAt,
                ContentHash = read.ContentHash,
                TextLength = read.TextLength,
                Status = read.Status,
                ErrorCode = read.ErrorCode,
                FetchedAt = DateTimeOffset.UtcNow,
            };
            if (await _repository.TryAddDocumentAsync(document, ct))
            {
                added++;
            }
        }

        // Giữ hash cũ nếu còn bài chưa đọc, để lượt sau không dừng sớm mà bỏ qua chúng.
        if (!retryLater)
        {
            source.LastContentHash = hash;
        }

        return new SourceOutcome(null, added);
    }

    private sealed record ReleaseRead(IReadOnlyList<FuelReading> Readings, string? ErrorCode);

    /// <summary>Trang chỉ mục → thông cáo mới nhất → giá. Thông cáo cũng phải được <c>robots.txt</c> cho phép.</summary>
    private async Task<ReleaseRead> ReadReleaseAsync(string indexHtml, Uri indexUri, MarketDataSettings settings, CancellationToken ct)
    {
        var releaseUri = FuelReleaseParser.FindLatestRelease(indexHtml, indexUri, settings);
        if (releaseUri is null)
        {
            return new ReleaseRead([], MarketErrorCodes.ReleaseNotFound);
        }

        var (rules, robotsError) = await RobotsForAsync(releaseUri, ct);
        if (rules is null)
        {
            return new ReleaseRead([], robotsError);
        }

        if (!rules.IsAllowed(releaseUri.PathAndQuery))
        {
            return new ReleaseRead([], MarketErrorCodes.RobotsDisallowed);
        }

        var fetched = await _fetcher.FetchAsync(releaseUri, ct);
        if (fetched.Outcome != WebFetchOutcome.Ok)
        {
            return new ReleaseRead([], ErrorCodeFor(fetched.Outcome));
        }

        var readings = FuelReleaseParser.Parse(HtmlTextExtractor.PageText(fetched.Content ?? string.Empty), settings);
        return new ReleaseRead(readings, FuelReadingValidator.ValidateRelease(readings, settings));
    }

    private sealed record ArticleCandidate(string Url, string Title, DateTimeOffset? PublishedAt);

    private static IEnumerable<ArticleCandidate> Candidates(MarketSource source, string content, Uri baseUri, MarketDataSettings settings)
    {
        if (source.Kind == MarketSourceKind.Feed)
        {
            return FeedReader.Read(content)
                .Where(item => item.Url.Length <= MaxUrlLength && MarketTopics.IsRelevant(settings, item.Title, item.Summary))
                .Select(item => new ArticleCandidate(item.Url, item.Title, item.PublishedAt))
                .DistinctBy(c => c.Url);
        }

        return HtmlTextExtractor.Links(content, baseUri)
            .Where(link => link.Url.Length <= MaxUrlLength && SameSite(link.Url, baseUri) && MarketTopics.IsRelevant(settings, link.Text))
            .Select(link => new ArticleCandidate(link.Url, link.Text, null))
            .DistinctBy(c => c.Url);
    }

    private sealed record ArticleRead(MarketDocumentStatus Status, int TextLength, string? ContentHash, string? ErrorCode, bool IsTemporary, DateTimeOffset? PublishedAt = null);

    private async Task<ArticleRead> ReadArticleAsync(ArticleCandidate candidate, CancellationToken ct)
    {
        var uri = new Uri(candidate.Url);
        var (rules, robotsError) = await RobotsForAsync(uri, ct);
        if (rules is null)
        {
            return robotsError == MarketErrorCodes.AddressNotAllowed
                ? new ArticleRead(MarketDocumentStatus.Blocked, 0, null, robotsError, false)
                : new ArticleRead(MarketDocumentStatus.Failed, 0, null, robotsError, true);
        }

        if (!rules.IsAllowed(uri.PathAndQuery))
        {
            return new ArticleRead(MarketDocumentStatus.Blocked, 0, null, MarketErrorCodes.RobotsDisallowed, false);
        }

        var fetched = await _fetcher.FetchAsync(uri, ct);
        switch (fetched.Outcome)
        {
            case WebFetchOutcome.Ok:
                var text = HtmlTextExtractor.ArticleText(fetched.Content ?? string.Empty);
                if (text.Length < HtmlTextExtractor.MinArticleLength)
                {
                    return new ArticleRead(MarketDocumentStatus.NoContent, text.Length, null, MarketErrorCodes.NoContent, false);
                }

                var publishedAt = HtmlTextExtractor.PublishedAt(fetched.Content ?? string.Empty);

                // Xét theo trang chứa bài, không theo nguồn: RSS tổng hợp có thể trỏ tới trang không cho phép dùng với AI.
                if (!rules.AiAllowed)
                {
                    return new ArticleRead(MarketDocumentStatus.AiNotAllowed, text.Length, Hash(text), null, false, publishedAt);
                }

                _freshTexts[candidate.Url] = text;
                return new ArticleRead(MarketDocumentStatus.PendingAi, text.Length, Hash(text), null, false, publishedAt);
            case WebFetchOutcome.Refused or WebFetchOutcome.AddressNotAllowed:
                return new ArticleRead(MarketDocumentStatus.Blocked, 0, null, ErrorCodeFor(fetched.Outcome), false);
            case WebFetchOutcome.Failed or WebFetchOutcome.TooManyRedirects:
                return new ArticleRead(MarketDocumentStatus.Failed, 0, null, ErrorCodeFor(fetched.Outcome), true);
            default:
                return new ArticleRead(MarketDocumentStatus.Failed, 0, null, ErrorCodeFor(fetched.Outcome), false);
        }
    }

    private sealed record Inspection(MarketSourceKind Kind, bool AiAllowed, string? ErrorCode, IReadOnlyList<string> Warnings);

    private async Task<Inspection> InspectAsync(Uri uri, MarketDataSettings settings, CancellationToken ct)
    {
        var (rules, robotsError) = await RobotsForAsync(uri, ct);
        if (rules is null)
        {
            return new Inspection(MarketSourceKind.Page, false, robotsError, []);
        }

        if (!rules.IsAllowed(uri.PathAndQuery))
        {
            return new Inspection(MarketSourceKind.Page, false, MarketErrorCodes.RobotsDisallowed, []);
        }

        var fetched = await _fetcher.FetchAsync(uri, ct);
        if (fetched.Outcome != WebFetchOutcome.Ok)
        {
            return new Inspection(MarketSourceKind.Page, false, ErrorCodeFor(fetched.Outcome), []);
        }

        var content = fetched.Content ?? string.Empty;
        var warnings = new List<string>();
        var official = MarketTrust.TierOf(uri.AbsoluteUri, settings) == MarketTrustedDomain.Official;
        var kind = DetectKind(content, settings, official ? fetched.FinalUri ?? uri : null);
        if (!rules.AiAllowed)
        {
            warnings.Add("AI_NOT_ALLOWED");
        }

        if (kind == MarketSourceKind.Page)
        {
            var finalUri = fetched.FinalUri ?? uri;
            var links = HtmlTextExtractor.Links(content, finalUri).Where(l => SameSite(l.Url, finalUri)).ToList();
            if (links.Count == 0 && HtmlTextExtractor.PageText(content).Length < HtmlTextExtractor.MinArticleLength)
            {
                warnings.Add(MarketErrorCodes.NoContent);
            }
            else if (!links.Any(l => MarketTopics.IsRelevant(settings, l.Text)))
            {
                warnings.Add(MarketErrorCodes.NoRelevantLinks);
            }
        }

        return new Inspection(kind, rules.AiAllowed, null, warnings);
    }

    /// <param name="officialUri">Địa chỉ trang nếu nguồn là hạng official: chỉ khi đó mới xét trang có phải chỉ mục thông cáo giá xăng không.</param>
    internal static MarketSourceKind DetectKind(string content, MarketDataSettings settings, Uri? officialUri = null)
    {
        if (FeedReader.LooksLikeFeed(content))
        {
            return MarketSourceKind.Feed;
        }

        var text = HtmlTextExtractor.PageText(content);
        if (FuelReadingValidator.ValidateTable(FuelTableParser.Parse(text), settings) is null || GoldTableParser.Parse(text, settings.Gold).Count > 0)
        {
            return MarketSourceKind.PriceTable;
        }

        return officialUri is not null && FuelReleaseParser.FindLatestRelease(content, officialUri, settings) is not null
            ? MarketSourceKind.FuelRelease
            : MarketSourceKind.Page;
    }

    private readonly record struct RobotsLookup(RobotsRules? Rules, string? ErrorCode);

    /// <summary>Không có luật khi chưa biết được trang cho phép gì (lỗi mạng, lỗi máy chủ) hoặc địa chỉ không được phép: không đọc.</summary>
    private async Task<RobotsLookup> RobotsForAsync(Uri uri, CancellationToken ct)
    {
        if (_robots.TryGetValue(uri.Host, out var cached))
        {
            return cached;
        }

        var fetched = await _fetcher.FetchAsync(new Uri($"https://{uri.IdnHost}/robots.txt"), ct);
        RobotsLookup lookup = fetched.Outcome switch
        {
            WebFetchOutcome.Ok => new RobotsLookup(RobotsRules.Parse(fetched.Content ?? string.Empty), null),
            WebFetchOutcome.NotFound or WebFetchOutcome.UnsupportedContent => new RobotsLookup(RobotsRules.AllowAll, null),
            WebFetchOutcome.Refused => new RobotsLookup(RobotsRules.DenyAll, null),
            WebFetchOutcome.AddressNotAllowed => new RobotsLookup(null, MarketErrorCodes.AddressNotAllowed),
            _ => new RobotsLookup(null, MarketErrorCodes.RobotsUnavailable),
        };
        _robots[uri.Host] = lookup;
        return lookup;
    }

    private static Uri ParseUrl(string? url)
    {
        var text = url?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxUrlLength
            || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new BusinessRuleException(MarketErrorCodes.InvalidUrl, "Link phải là địa chỉ https đầy đủ.");
        }

        // Giữ nguyên cổng lạ để bước tải trang từ chối, thay vì âm thầm đổi thành 443.
        return new UriBuilder(uri) { Fragment = string.Empty }.Uri;
    }

    private static bool SameSite(string url, Uri baseUri)
        => Uri.TryCreate(url, UriKind.Absolute, out var target) && MarketDomains.OfHost(target.Host) == MarketDomains.OfHost(baseUri.Host);

    private static string ErrorCodeFor(WebFetchOutcome outcome) => outcome switch
    {
        WebFetchOutcome.AddressNotAllowed => MarketErrorCodes.AddressNotAllowed,
        WebFetchOutcome.Refused => MarketErrorCodes.Refused,
        WebFetchOutcome.NotFound => MarketErrorCodes.NotFound,
        WebFetchOutcome.TooLarge => MarketErrorCodes.TooLarge,
        WebFetchOutcome.UnsupportedContent => MarketErrorCodes.UnsupportedContent,
        _ => MarketErrorCodes.Unreachable,
    };

    private static string Explain(string errorCode) => errorCode switch
    {
        MarketErrorCodes.AddressNotAllowed => "Link trỏ tới địa chỉ không được phép.",
        MarketErrorCodes.NotTrusted => "Tên miền này chưa có trong danh sách nguồn uy tín. Thêm tên miền kèm đơn vị chủ quản và số giấy phép ở Cấu hình hệ thống trước.",
        MarketErrorCodes.RobotsDisallowed => "Trang này không cho phép công cụ tự động đọc. Hệ thống không đọc những trang như vậy, gồm cả trang cần đăng nhập như Facebook.",
        MarketErrorCodes.RobotsUnavailable => "Chưa xác định được quy định của trang này. Hãy thử lại sau.",
        MarketErrorCodes.Refused => "Trang từ chối công cụ tự động hoặc cần đăng nhập. Hệ thống không vượt qua việc này.",
        MarketErrorCodes.NotFound => "Không tìm thấy trang.",
        MarketErrorCodes.TooLarge => "Trang quá lớn.",
        MarketErrorCodes.UnsupportedContent => "Link không phải trang web hoặc RSS.",
        _ => "Không tải được trang. Hãy kiểm tra link hoặc thử lại sau.",
    };

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string Limit(string text, int max) => text.Length <= max ? text : text[..max];

    private static MarketSourceDto ToDto(MarketSource s, MarketDataSettings settings)
        => new(s.Id, s.Name, s.Url, MarketText.Kind(s.Kind), s.AiAllowed, s.IsActive, s.LastCheckedAt, s.LastSuccessAt, s.ConsecutiveFailures, s.LastErrorCode, s.CreatedAt, MarketTrust.TierOf(s.Url, settings));

    private static FuelPriceDto ToDto(FuelPrice p) => new(p.Id, p.FuelKey, p.Region1PriceCents, p.Region2PriceCents, p.FirstSeenAt, p.Origin == FuelPriceOrigin.Manual ? "manual" : "sources", p.Note, p.SourceDomains);

    private static FuelReadingDto ToDto(FuelReading r) => new(r.FuelKey, r.Region1PriceCents, r.Region2PriceCents);

    private static GoldPriceDto ToDto(GoldPrice p) => new(p.Id, p.GoldKey, p.BuyCents, p.SellCents, p.FirstSeenAt, p.Origin == FuelPriceOrigin.Manual ? "manual" : "sources", p.Note, p.SourceDomains);

    private static GoldReadingDto ToDto(GoldReading r) => new(r.GoldKey, r.BuyCents, r.SellCents);
}
