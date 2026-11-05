using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class MarketDataRepository : IMarketDataRepository
{
    private const string UniqueViolation = "23505";

    private readonly FinMateDbContext _context;

    public MarketDataRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<List<MarketSource>> ListSourcesAsync(CancellationToken ct = default)
        => _context.MarketSources.AsNoTracking().OrderBy(s => s.CreatedAt).ToListAsync(ct);

    public Task<List<MarketSource>> ListActiveSourcesAsync(CancellationToken ct = default)
        => _context.MarketSources.Where(s => s.IsActive).OrderBy(s => s.CreatedAt).ToListAsync(ct);

    public Task<MarketSource?> GetSourceAsync(Guid id, CancellationToken ct = default)
        => _context.MarketSources.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<bool> TryAddSourceAsync(MarketSource source, CancellationToken ct = default)
    {
        _context.MarketSources.Add(source);
        try
        {
            await _context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _context.Entry(source).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<HashSet<string>> FindExistingDocumentUrlsAsync(IReadOnlyCollection<string> urls, CancellationToken ct = default)
    {
        var found = await _context.MarketDocuments.AsNoTracking().Where(d => urls.Contains(d.Url)).Select(d => d.Url).ToListAsync(ct);
        return found.ToHashSet();
    }

    public async Task<bool> TryAddDocumentAsync(MarketDocument document, CancellationToken ct = default)
    {
        _context.MarketDocuments.Add(document);
        try
        {
            await _context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _context.Entry(document).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<(List<MarketDocument> Items, int Total)> ListDocumentsAsync(Guid? sourceId, MarketDocumentStatus? status, int offset, int limit, CancellationToken ct = default)
    {
        var query = _context.MarketDocuments.AsNoTracking();
        if (sourceId is { } source)
        {
            query = query.Where(d => d.SourceId == source);
        }

        if (status is { } wanted)
        {
            query = query.Where(d => d.Status == wanted);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(d => d.FetchedAt).ThenBy(d => d.Id).Skip(offset).Take(limit).ToListAsync(ct);
        return (items, total);
    }

    public Task<int> DeleteDocumentsBeforeAsync(DateTimeOffset before, CancellationToken ct = default)
        => _context.MarketDocuments.Where(d => d.FetchedAt < before).ExecuteDeleteAsync(ct);

    public async Task<List<FuelPrice>> ListLatestFuelPricesAsync(CancellationToken ct = default)
    {
        var keys = await _context.FuelPrices.AsNoTracking().Select(p => p.FuelKey).Distinct().ToListAsync(ct);
        var latest = new List<FuelPrice>(keys.Count);
        foreach (var key in keys)
        {
            latest.Add(await _context.FuelPrices.AsNoTracking()
                .Where(p => p.FuelKey == key)
                .OrderByDescending(p => p.FirstSeenAt)
                .ThenByDescending(p => p.Id)
                .FirstAsync(ct));
        }

        return latest;
    }

    public async Task<(List<FuelPrice> Items, int Total)> ListFuelPricesAsync(string? fuelKey, int offset, int limit, CancellationToken ct = default)
    {
        var query = _context.FuelPrices.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(fuelKey))
        {
            query = query.Where(p => p.FuelKey == fuelKey);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(p => p.FirstSeenAt).ThenBy(p => p.FuelKey).Skip(offset).Take(limit).ToListAsync(ct);
        return (items, total);
    }

    public void AddFuelPrice(FuelPrice price) => _context.FuelPrices.Add(price);

    public Task<int> MarkPendingDocumentsNotAllowedAsync(CancellationToken ct = default)
        => _context.MarketDocuments
            .Where(d => d.Status == MarketDocumentStatus.PendingAi
                && _context.MarketSources.Any(s => s.Id == d.SourceId && !s.AiAllowed))
            .ExecuteUpdateAsync(set => set.SetProperty(d => d.Status, MarketDocumentStatus.AiNotAllowed), ct);

    public Task<List<MarketDocument>> ListDocumentsForAiAsync(int limit, CancellationToken ct = default)
        => _context.MarketDocuments
            .Where(d => d.Status == MarketDocumentStatus.PendingAi
                && _context.MarketSources.Any(s => s.Id == d.SourceId && s.AiAllowed))
            .OrderByDescending(d => d.FetchedAt)
            .ThenBy(d => d.Id)
            .Take(limit)
            .ToListAsync(ct);

    public Task<int> CountAiAttemptsSinceAsync(DateTimeOffset since, CancellationToken ct = default)
        => _context.MarketDocuments.CountAsync(d => d.AiAttemptedAt >= since, ct);

    public void AddSignal(MarketSignal signal) => _context.MarketSignals.Add(signal);

    public Task<List<FuelPrice>> ListFuelPricesAwaitingAlertsAsync(string fuelKey, CancellationToken ct = default)
        => _context.FuelPrices.Where(p => p.FuelKey == fuelKey && p.AlertsProcessedAt == null).OrderBy(p => p.FirstSeenAt).ToListAsync(ct);

    public Task<List<MarketEvent>> ListEventsAwaitingAlertsAsync(CancellationToken ct = default)
        => _context.MarketEvents.Where(e => e.Status == MarketEventStatus.Approved && e.AlertsProcessedAt == null).OrderBy(e => e.LastSeenAt).ToListAsync(ct);

    public async Task<FuelPrice?> GetFuelPriceAsOfAsync(string fuelKey, DateTimeOffset at, CancellationToken ct = default)
    {
        var query = _context.FuelPrices.AsNoTracking().Where(p => p.FuelKey == fuelKey);
        return await query.Where(p => p.FirstSeenAt <= at).OrderByDescending(p => p.FirstSeenAt).FirstOrDefaultAsync(ct)
            ?? await query.OrderBy(p => p.FirstSeenAt).FirstOrDefaultAsync(ct);
    }

    public async Task<List<(string Url, DateTimeOffset SeenAt)>> ListActualSignalSourcesAsync(MarketTopic topic, MarketDirection direction, DateTimeOffset since, CancellationToken ct = default)
    {
        var rows = await SignalRows(topic, direction, since)
            .Select(row => new { row.Document.Url, SeenAt = row.Document.PublishedAt ?? row.Document.FetchedAt })
            .ToListAsync(ct);
        return rows.Select(r => (r.Url, r.SeenAt)).ToList();
    }

    public Task<MarketEvent?> GetLatestEventAsync(MarketTopic topic, MarketDirection direction, DateTimeOffset since, CancellationToken ct = default)
        => _context.MarketEvents
            .Where(e => e.Topic == topic && e.Direction == direction && e.LastSeenAt >= since && e.Status != MarketEventStatus.Expired)
            .OrderByDescending(e => e.LastSeenAt)
            .FirstOrDefaultAsync(ct);

    public async Task<bool> TryAddEventAsync(MarketEvent marketEvent, CancellationToken ct = default)
    {
        _context.MarketEvents.Add(marketEvent);
        try
        {
            await _context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _context.Entry(marketEvent).State = EntityState.Detached;
            return false;
        }
    }

    public Task<int> ExpireEventsAsync(DateTimeOffset lastSeenBefore, CancellationToken ct = default)
        => _context.MarketEvents
            .Where(e => (e.Status == MarketEventStatus.Pending || e.Status == MarketEventStatus.Approved) && e.LastSeenAt < lastSeenBefore)
            .ExecuteUpdateAsync(set => set.SetProperty(e => e.Status, MarketEventStatus.Expired), ct);

    public async Task<(List<MarketEvent> Items, int Total)> ListEventsAsync(MarketEventStatus? status, int offset, int limit, CancellationToken ct = default)
    {
        var query = _context.MarketEvents.AsNoTracking();
        if (status is { } wanted)
        {
            query = query.Where(e => e.Status == wanted);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(e => e.LastSeenAt).ThenBy(e => e.Id).Skip(offset).Take(limit).ToListAsync(ct);
        return (items, total);
    }

    public Task<List<MarketSignalSource>> ListEventSignalsAsync(MarketTopic topic, MarketDirection direction, DateTimeOffset since, CancellationToken ct = default)
        => SignalRows(topic, direction, since)
            .OrderByDescending(row => row.Signal.CreatedAt)
            .Select(row => new MarketSignalSource(
                row.Document.Title,
                row.Document.Url,
                row.Signal.Evidence,
                row.Signal.ChangePercent,
                row.Signal.EffectiveDate,
                row.Document.PublishedAt,
                _context.MarketSources.Where(source => source.Id == row.Document.SourceId).Select(source => source.Name).First()))
            .ToListAsync(ct);

    public async Task<bool> TryReviewEventAsync(Guid id, MarketEventStatus status, Guid adminId, string? note, DateTimeOffset now, CancellationToken ct = default)
        => await _context.MarketEvents
            .Where(e => e.Id == id && e.Status == MarketEventStatus.Pending)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(e => e.Status, status)
                    .SetProperty(e => e.ReviewedByUserId, adminId)
                    .SetProperty(e => e.ReviewedAt, now)
                    .SetProperty(e => e.ReviewNote, note),
                ct) == 1;

    public Task<MarketEvent?> GetEventAsync(Guid id, CancellationToken ct = default)
        => _context.MarketEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    private sealed class SignalRow
    {
        public MarketSignal Signal { get; init; } = null!;
        public MarketDocument Document { get; init; } = null!;
    }

    private IQueryable<SignalRow> SignalRows(MarketTopic topic, MarketDirection direction, DateTimeOffset since)
        => _context.MarketSignals.AsNoTracking()
            .Where(s => s.Topic == topic && s.Direction == direction && s.FactStatus == MarketFactStatus.Actual)
            .Join(_context.MarketDocuments.AsNoTracking(), s => s.DocumentId, d => d.Id, (s, d) => new SignalRow { Signal = s, Document = d })
            .Where(row => (row.Document.PublishedAt ?? row.Document.FetchedAt) >= since);

    public Task SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
}
