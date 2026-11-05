using System.Security.Cryptography;
using System.Text;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
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
    DateTimeOffset CreatedAt);

public record CreatedMarketSourceDto(MarketSourceDto Source, IReadOnlyList<string> Warnings);

public record BulkSourceResult(string Url, bool Created, MarketSourceDto? Source, IReadOnlyList<string> Warnings, string? ErrorCode, string? ErrorMessage);

public record MarketDocumentDto(Guid Id, Guid SourceId, string Url, string Title, DateTimeOffset? PublishedAt, int TextLength, string Status, string? ErrorCode, DateTimeOffset FetchedAt);

public record MarketDocumentPageDto(IReadOnlyList<MarketDocumentDto> Items, int Total, int Offset, int Limit);

public record FuelPriceDto(Guid Id, string FuelKey, long Region1PriceCents, long Region2PriceCents, DateTimeOffset FirstSeenAt, string Origin, string? Note);

public record FuelPricePageDto(IReadOnlyList<FuelPriceDto> Items, int Total, int Offset, int Limit);

public record FuelReadingDto(string FuelKey, long Region1PriceCents, long Region2PriceCents);

public record ArticlePreviewDto(string Url, string Title, int TextLength, string Status);

public record MarketSourceTestDto(string Kind, bool AiAllowed, IReadOnlyList<FuelReadingDto> FuelPrices, string? FuelErrorCode, IReadOnlyList<ArticlePreviewDto> Articles, IReadOnlyList<string> Warnings);

public record MarketRefreshSummary(int SourcesChecked, int SourcesFailed, int NewDocuments, IReadOnlyList<string> ConfirmedFuels, IReadOnlyList<string> HeldBackFuels);

public sealed record UpdateMarketSourceInput(string? Name, MarketSourceKind? Kind, bool? IsActive);

public sealed record CorrectFuelPriceInput(string FuelKey, long Region1PriceCents, long Region2PriceCents, string? Note);

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
    public const string NoContent = "NO_CONTENT";
    public const string NoRelevantLinks = "NO_RELEVANT_LINKS";
    public const string BulkTooLarge = "MARKET_SOURCE_BULK_TOO_LARGE";
    public const string UnknownFuel = "FUEL_KEY_UNKNOWN";
    public const string NoteRequired = "FUEL_NOTE_REQUIRED";
    public const string NoChange = "FUEL_PRICE_UNCHANGED";
}

public static class MarketText
{
    public static string Kind(MarketSourceKind kind) => kind switch
    {
        MarketSourceKind.Feed => "feed",
        MarketSourceKind.PriceTable => "price_table",
        MarketSourceKind.Page => "page",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static MarketSourceKind? ParseKind(string? text) => text switch
    {
        "feed" => MarketSourceKind.Feed,
        "price_table" => MarketSourceKind.PriceTable,
        "page" => MarketSourceKind.Page,
        _ => null,
    };

    public static string Status(MarketDocumentStatus status) => status switch
    {
        MarketDocumentStatus.PendingAi => "pending_ai",
        MarketDocumentStatus.NoContent => "no_content",
        MarketDocumentStatus.Blocked => "blocked",
        MarketDocumentStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static MarketDocumentStatus? ParseStatus(string? text) => text switch
    {
        "pending_ai" => MarketDocumentStatus.PendingAi,
        "no_content" => MarketDocumentStatus.NoContent,
        "blocked" => MarketDocumentStatus.Blocked,
        "failed" => MarketDocumentStatus.Failed,
        _ => null,
    };
}

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
    private readonly ILogger<MarketDataService> _logger;
    private readonly Dictionary<string, RobotsLookup> _robots = new(StringComparer.OrdinalIgnoreCase);

    public MarketDataService(IMarketDataRepository repository, IWebPageFetcher fetcher, ISystemConfigProvider config, IAuditLogService audit, ILogger<MarketDataService> logger)
    {
        _repository = repository;
        _fetcher = fetcher;
        _config = config;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MarketSourceDto>> ListSourcesAsync(CancellationToken ct = default)
        => (await _repository.ListSourcesAsync(ct)).Select(ToDto).ToList();

    public async Task<CreatedMarketSourceDto> CreateSourceAsync(Guid adminId, string? ip, string? url, string? name, CancellationToken ct = default)
    {
        var uri = ParseUrl(url);
        var inspection = await InspectAsync(uri, ct);
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
        return new CreatedMarketSourceDto(ToDto(source), inspection.Warnings);
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

        if (input.Kind is { } kind)
        {
            source.Kind = kind;
        }

        if (input.IsActive is { } active)
        {
            source.IsActive = active;
        }

        source.UpdatedAt = DateTimeOffset.UtcNow;
        await _repository.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.AdminMarketSourceUpdated, adminId, ip, new { source.Id, Kind = MarketText.Kind(source.Kind), source.IsActive }, ct);
        return ToDto(source);
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
            var readings = FuelTableParser.Parse(HtmlTextExtractor.PageText(content));
            var error = FuelReadingValidator.ValidateTable(readings, settings);
            return new MarketSourceTestDto(MarketText.Kind(source.Kind), rules.AiAllowed, readings.Select(ToDto).ToList(), error, [], []);
        }

        var candidates = Candidates(source, content, fetched.FinalUri ?? uri).ToList();
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
        return ToDto(price);
    }

    public async Task<MarketRefreshSummary> RefreshAsync(CancellationToken ct = default)
    {
        var settings = (await _config.GetAsync(ct)).Document.MarketData;
        var sources = await _repository.ListActiveSourcesAsync(ct);
        var tables = new List<IReadOnlyList<FuelReading>>();
        var failed = 0;
        var newDocuments = 0;

        foreach (var source in sources)
        {
            var now = DateTimeOffset.UtcNow;
            source.LastCheckedAt = now;
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
                        tables.Add(outcome.Readings);
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
        return new MarketRefreshSummary(sources.Count, failed, newDocuments, confirmation.Confirmed.Select(c => c.FuelKey).ToList(), confirmation.HeldBack);
    }

    public Task<int> PurgeOldDocumentsAsync(CancellationToken ct = default)
        => _repository.DeleteDocumentsBeforeAsync(DateTimeOffset.UtcNow.AddDays(-DocumentRetentionDays), ct);

    private async Task<FuelConfirmation> ConfirmPricesAsync(IReadOnlyList<IReadOnlyList<FuelReading>> tables, MarketDataSettings settings, CancellationToken ct)
    {
        var latest = (await _repository.ListLatestFuelPricesAsync(ct))
            .ToDictionary(p => p.FuelKey, p => new FuelReading(p.FuelKey, p.Region1PriceCents, p.Region2PriceCents));
        var confirmation = FuelPriceConfirmer.Confirm(tables, latest, settings);
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
            });
        }

        await _repository.SaveChangesAsync(ct);
        if (confirmation.HeldBack.Count > 0)
        {
            _logger.LogWarning("Fuel prices held back as implausible: {FuelKeys}", string.Join(",", confirmation.HeldBack));
        }

        return confirmation;
    }

    private sealed record SourceOutcome(string? ErrorCode, int NewDocuments = 0, IReadOnlyList<FuelReading>? Readings = null);

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
            var readings = FuelTableParser.Parse(HtmlTextExtractor.PageText(content));
            var error = FuelReadingValidator.ValidateTable(readings, settings);
            return error is null ? new SourceOutcome(null, 0, readings) : new SourceOutcome(error);
        }

        var hash = Hash(content);
        if (hash == source.LastContentHash)
        {
            return new SourceOutcome(null);
        }

        var candidates = Candidates(source, content, fetched.FinalUri ?? uri).ToList();
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
                PublishedAt = candidate.PublishedAt,
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

    private sealed record ArticleCandidate(string Url, string Title, DateTimeOffset? PublishedAt);

    private static IEnumerable<ArticleCandidate> Candidates(MarketSource source, string content, Uri baseUri)
    {
        if (source.Kind == MarketSourceKind.Feed)
        {
            return FeedReader.Read(content)
                .Where(item => MarketTopics.IsRelevant(item.Title, item.Summary))
                .Select(item => new ArticleCandidate(item.Url, item.Title, item.PublishedAt))
                .DistinctBy(c => c.Url);
        }

        return HtmlTextExtractor.Links(content, baseUri)
            .Where(link => SameSite(link.Url, baseUri) && MarketTopics.IsRelevant(link.Text))
            .Select(link => new ArticleCandidate(link.Url, link.Text, null))
            .DistinctBy(c => c.Url);
    }

    private sealed record ArticleRead(MarketDocumentStatus Status, int TextLength, string? ContentHash, string? ErrorCode, bool IsTemporary);

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
                return text.Length < HtmlTextExtractor.MinArticleLength
                    ? new ArticleRead(MarketDocumentStatus.NoContent, text.Length, null, MarketErrorCodes.NoContent, false)
                    : new ArticleRead(MarketDocumentStatus.PendingAi, text.Length, Hash(text), null, false);
            case WebFetchOutcome.Refused or WebFetchOutcome.AddressNotAllowed:
                return new ArticleRead(MarketDocumentStatus.Blocked, 0, null, ErrorCodeFor(fetched.Outcome), false);
            case WebFetchOutcome.Failed or WebFetchOutcome.TooManyRedirects:
                return new ArticleRead(MarketDocumentStatus.Failed, 0, null, ErrorCodeFor(fetched.Outcome), true);
            default:
                return new ArticleRead(MarketDocumentStatus.Failed, 0, null, ErrorCodeFor(fetched.Outcome), false);
        }
    }

    private sealed record Inspection(MarketSourceKind Kind, bool AiAllowed, string? ErrorCode, IReadOnlyList<string> Warnings);

    private async Task<Inspection> InspectAsync(Uri uri, CancellationToken ct)
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

        var settings = (await _config.GetAsync(ct)).Document.MarketData;
        var content = fetched.Content ?? string.Empty;
        var warnings = new List<string>();
        var kind = DetectKind(content, settings);
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
            else if (!links.Any(l => MarketTopics.IsRelevant(l.Text)))
            {
                warnings.Add(MarketErrorCodes.NoRelevantLinks);
            }
        }

        return new Inspection(kind, rules.AiAllowed, null, warnings);
    }

    internal static MarketSourceKind DetectKind(string content, MarketDataSettings settings)
    {
        if (FeedReader.LooksLikeFeed(content))
        {
            return MarketSourceKind.Feed;
        }

        var readings = FuelTableParser.Parse(HtmlTextExtractor.PageText(content));
        return FuelReadingValidator.ValidateTable(readings, settings) is null ? MarketSourceKind.PriceTable : MarketSourceKind.Page;
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
        => Uri.TryCreate(url, UriKind.Absolute, out var target) && BareHost(target) == BareHost(baseUri);

    private static string BareHost(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

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

    private static MarketSourceDto ToDto(MarketSource s) => new(s.Id, s.Name, s.Url, MarketText.Kind(s.Kind), s.AiAllowed, s.IsActive, s.LastCheckedAt, s.LastSuccessAt, s.ConsecutiveFailures, s.LastErrorCode, s.CreatedAt);

    private static FuelPriceDto ToDto(FuelPrice p) => new(p.Id, p.FuelKey, p.Region1PriceCents, p.Region2PriceCents, p.FirstSeenAt, p.Origin == FuelPriceOrigin.Manual ? "manual" : "sources", p.Note);

    private static FuelReadingDto ToDto(FuelReading r) => new(r.FuelKey, r.Region1PriceCents, r.Region2PriceCents);
}
