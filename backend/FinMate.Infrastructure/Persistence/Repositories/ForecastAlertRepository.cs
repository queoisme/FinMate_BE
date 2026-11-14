using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class ForecastAlertRepository : IForecastAlertRepository
{
    private readonly FinMateDbContext _context;

    public ForecastAlertRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<bool> ExistsAsync(Guid userId, ForecastAlertKind kind, string dedupKey, CancellationToken ct = default)
        => _context.ForecastAlerts.AnyAsync(a => a.UserId == userId && a.Kind == kind && a.DedupKey == dedupKey, ct);

    public Task<bool> AnyCreatedSinceAsync(Guid userId, ForecastAlertKind kind, DateTimeOffset since, CancellationToken ct = default)
        => _context.ForecastAlerts.AnyAsync(a => a.UserId == userId && a.Kind == kind && a.CreatedAt >= since, ct);

    public Task<bool> AnyWithKeyPrefixSinceAsync(Guid userId, ForecastAlertKind kind, string dedupKeyPrefix, DateTimeOffset since, CancellationToken ct = default)
        => _context.ForecastAlerts.AnyAsync(a => a.UserId == userId && a.Kind == kind && a.CreatedAt >= since && a.DedupKey.StartsWith(dedupKeyPrefix), ct);

    public async Task<IReadOnlyList<DateTimeOffset>> ListCreatedWithKeyPrefixAsync(Guid userId, ForecastAlertKind kind, string dedupKeyPrefix, CancellationToken ct = default)
        => await _context.ForecastAlerts
            .Where(a => a.UserId == userId && a.Kind == kind && a.DedupKey.StartsWith(dedupKeyPrefix))
            .OrderBy(a => a.CreatedAt)
            .Select(a => a.CreatedAt)
            .ToListAsync(ct);

    public Task<bool> AnyMarkedInaccurateSinceAsync(Guid userId, ForecastAlertKind kind, DateTimeOffset since, CancellationToken ct = default)
        => _context.ForecastAlerts.AnyAsync(
            a => a.UserId == userId && a.Kind == kind && a.MarkedInaccurate == true && a.FeedbackAt >= since, ct);

    public async Task<bool> TryAddAsync(ForecastAlert alert, CancellationToken ct = default)
    {
        _context.ForecastAlerts.Add(alert);
        try
        {
            await _context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Một lượt kiểm tra khác vừa ghi cùng (user, kind, dedup_key): cảnh báo đã có, không phải lỗi.
            _context.Entry(alert).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<IReadOnlyList<ForecastAlert>> ListDuePendingAsync(DateTimeOffset now, int limit, CancellationToken ct = default)
        => await _context.ForecastAlerts
            .Where(a => a.Status == ForecastAlertStatus.Pending && a.ScheduledFor <= now)
            .OrderBy(a => a.ScheduledFor)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<ForecastAlertPage> ListForUserAsync(Guid userId, string? cursor, int limit, CancellationToken ct = default)
    {
        var query = _context.ForecastAlerts.AsNoTracking().Where(a => a.UserId == userId);

        if (KeysetCursor.TryDecode(cursor, out var cursorAt, out var cursorId))
        {
            query = query.Where(a =>
                a.CreatedAt < cursorAt || (a.CreatedAt == cursorAt && a.Id.CompareTo(cursorId) < 0));
        }

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Take(limit + 1)
            .ToListAsync(ct);

        string? nextCursor = null;
        if (items.Count > limit)
        {
            var last = items[limit - 1];
            nextCursor = KeysetCursor.Encode(last.CreatedAt, last.Id);
            items = items.Take(limit).ToList();
        }

        return new ForecastAlertPage(items, nextCursor);
    }

    public Task<ForecastAlert?> GetAsync(Guid id, Guid userId, CancellationToken ct = default)
        => _context.ForecastAlerts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);

    public async Task UpdateAsync(ForecastAlert alert, CancellationToken ct = default)
    {
        _context.ForecastAlerts.Update(alert);
        await _context.SaveChangesAsync(ct);
    }
}
