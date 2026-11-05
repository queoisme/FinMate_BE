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

    public Task SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
}
