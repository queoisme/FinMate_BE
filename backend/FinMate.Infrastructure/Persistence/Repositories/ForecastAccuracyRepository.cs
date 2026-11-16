using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class ForecastAccuracyRepository(FinMateDbContext context) : IForecastAccuracyRepository
{
    public Task<bool> HasSnapshotsAsync(Guid userId, DateOnly originDate, CancellationToken ct = default)
        => context.ForecastSnapshots.AnyAsync(s => s.UserId == userId && s.OriginDate == originDate, ct);

    public async Task AddSnapshotsAsync(IReadOnlyList<ForecastSnapshot> snapshots, CancellationToken ct = default)
        => await context.ForecastSnapshots.AddRangeAsync(snapshots, ct);

    public async Task<IReadOnlyList<ForecastSnapshot>> ListAwaitingActualAsync(DateOnly before, int limit, CancellationToken ct = default)
        => await context.ForecastSnapshots
            .Where(s => s.ActualCents == null && s.HorizonDate < before)
            .OrderBy(s => s.HorizonDate)
            .Take(limit)
            .ToListAsync(ct);

    public Task<int> CountPendingAsync(DateOnly from, CancellationToken ct = default)
        => context.ForecastSnapshots.CountAsync(s => s.ActualCents == null && s.HorizonDate >= from, ct);

    public async Task<IReadOnlyList<ForecastSnapshot>> ListFilledSinceAsync(DateOnly from, CancellationToken ct = default)
        => await context.ForecastSnapshots.AsNoTracking().Where(s => s.ActualCents != null && s.OriginDate >= from).ToListAsync(ct);

    public Task<ForecastSnapshot?> GetLatestForUserAsync(Guid userId, CancellationToken ct = default)
        => context.ForecastSnapshots
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.OriginDate)
            .ThenByDescending(s => s.HorizonDays)
            .FirstOrDefaultAsync(ct);

    public Task<ForecastFeedback?> GetNewFeedbackForSnapshotAsync(Guid userId, Guid snapshotId, CancellationToken ct = default)
        => context.ForecastFeedback.FirstOrDefaultAsync(f => f.UserId == userId && f.SnapshotId == snapshotId && f.Status == ForecastFeedbackStatuses.New, ct);

    public async Task AddFeedbackAsync(ForecastFeedback feedback, CancellationToken ct = default)
        => await context.ForecastFeedback.AddAsync(feedback, ct);

    public async Task<(IReadOnlyList<ForecastFeedback> Items, int Total)> ListFeedbackAsync(string? status, int limit, int offset, CancellationToken ct = default)
    {
        var query = context.ForecastFeedback.AsNoTracking().Where(f => status == null || f.Status == status);
        var total = await query.CountAsync(ct);
        var items = await query.Include(f => f.Snapshot).OrderByDescending(f => f.CreatedAt).Skip(offset).Take(limit).ToListAsync(ct);
        return (items, total);
    }

    public Task<ForecastFeedback?> GetFeedbackAsync(Guid id, CancellationToken ct = default)
        => context.ForecastFeedback.Include(f => f.Snapshot).FirstOrDefaultAsync(f => f.Id == id, ct);

    public async Task<IReadOnlyList<ForecastFeedback>> ListFeedbackSinceAsync(DateTimeOffset since, CancellationToken ct = default)
        => await context.ForecastFeedback.AsNoTracking().Where(f => f.CreatedAt >= since).ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
