using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinMate.Infrastructure.Persistence.Repositories;

public sealed class PersonalDataExportRepository(FinMateDbContext db) : IPersonalDataExportRepository
{
    private const string ActiveIndex = "ux_personal_data_exports_user_active";

    public Task<bool> HasActiveAsync(Guid userId, CancellationToken ct = default)
        => db.PersonalDataExports.AnyAsync(e => e.UserId == userId
            && (e.Status == PersonalDataExportStatus.Pending || e.Status == PersonalDataExportStatus.Running), ct);

    public async Task<IReadOnlyList<DateTimeOffset>> CountedRequestTimesSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default)
        => await db.PersonalDataExports.Where(e => e.UserId == userId && e.CountsTowardLimit && e.RequestedAt > since)
            .OrderBy(e => e.RequestedAt).Select(e => e.RequestedAt).ToListAsync(ct);

    public async Task<bool> TryAddAsync(PersonalDataExport export, CancellationToken ct = default)
    {
        db.PersonalDataExports.Add(export);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: ActiveIndex })
        {
            db.Entry(export).State = EntityState.Detached;
            return false;
        }
    }

    public Task<PersonalDataExport?> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
        => db.PersonalDataExports.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId, ct);

    public async Task<IReadOnlyList<PersonalDataExport>> ListAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default)
        => await db.PersonalDataExports.AsNoTracking().Where(e => e.UserId == userId && e.RequestedAt > since)
            .OrderByDescending(e => e.RequestedAt).ThenByDescending(e => e.Id).ToListAsync(ct);

    public async Task<PersonalDataExport?> TryLeaseAsync(Guid id, DateTimeOffset now, DateTimeOffset leaseUntil, int maxAttempts, CancellationToken ct = default)
    {
        var leased = await db.PersonalDataExports
            .Where(e => e.Id == id && e.Attempts < maxAttempts
                && (e.Status == PersonalDataExportStatus.Pending || (e.Status == PersonalDataExportStatus.Running && e.LeaseUntil < now)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, PersonalDataExportStatus.Running)
                .SetProperty(e => e.LeaseUntil, leaseUntil)
                .SetProperty(e => e.Attempts, e => e.Attempts + 1)
                .SetProperty(e => e.StartedAt, e => e.StartedAt ?? now), ct);
        return leased == 0 ? null : await db.PersonalDataExports.AsNoTracking().SingleAsync(e => e.Id == id, ct);
    }

    public async Task<bool> MarkReadyAsync(Guid id, byte[] ciphertext, byte[] nonce, long sizeBytes, DateTimeOffset readyAt, DateTimeOffset expiresAt,
        CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var updated = await db.PersonalDataExports.Where(e => e.Id == id && e.Status == PersonalDataExportStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, PersonalDataExportStatus.Ready)
                .SetProperty(e => e.ReadyAt, readyAt)
                .SetProperty(e => e.ExpiresAt, expiresAt)
                .SetProperty(e => e.SizeBytes, sizeBytes)
                .SetProperty(e => e.LeaseUntil, (DateTimeOffset?)null), ct);
        if (updated == 0)
        {
            return false;
        }

        db.PersonalDataExportArchives.Add(new PersonalDataExportArchive { ExportId = id, Data = ciphertext, Nonce = nonce, CreatedAt = readyAt });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<bool> MarkFailedAsync(Guid id, string failureCode, bool countsTowardLimit, DateTimeOffset now, CancellationToken ct = default)
        => await db.PersonalDataExports
            .Where(e => e.Id == id && (e.Status == PersonalDataExportStatus.Pending || e.Status == PersonalDataExportStatus.Running))
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, PersonalDataExportStatus.Failed)
                .SetProperty(e => e.FailureCode, failureCode)
                .SetProperty(e => e.FailedAt, now)
                .SetProperty(e => e.CountsTowardLimit, countsTowardLimit)
                .SetProperty(e => e.LeaseUntil, (DateTimeOffset?)null), ct) > 0;

    public Task ReleaseAsync(Guid id, CancellationToken ct = default)
        => db.PersonalDataExports.Where(e => e.Id == id && e.Status == PersonalDataExportStatus.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, PersonalDataExportStatus.Pending).SetProperty(e => e.LeaseUntil, (DateTimeOffset?)null), ct);

    public async Task<IReadOnlyList<PersonalDataExport>> FindStaleAsync(DateTimeOffset pendingBefore, DateTimeOffset now, CancellationToken ct = default)
        => await db.PersonalDataExports.AsNoTracking()
            .Where(e => (e.Status == PersonalDataExportStatus.Pending && e.RequestedAt < pendingBefore)
                || (e.Status == PersonalDataExportStatus.Running && e.LeaseUntil < now))
            .OrderBy(e => e.RequestedAt).Take(100).ToListAsync(ct);

    public async Task<bool> SetDownloadTokenAsync(Guid userId, Guid id, string tokenHash, DateTimeOffset expiresAt, CancellationToken ct = default)
        => await db.PersonalDataExports.Where(e => e.Id == id && e.UserId == userId && e.Status == PersonalDataExportStatus.Ready)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.DownloadTokenHash, tokenHash).SetProperty(e => e.DownloadTokenExpiresAt, expiresAt), ct) > 0;

    public async Task<bool> TryConsumeDownloadTokenAsync(Guid userId, Guid id, string tokenHash, DateTimeOffset now, CancellationToken ct = default)
        => await db.PersonalDataExports
            .Where(e => e.Id == id && e.UserId == userId && e.Status == PersonalDataExportStatus.Ready
                && e.DownloadTokenHash == tokenHash && e.DownloadTokenExpiresAt > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.DownloadTokenHash, (string?)null)
                .SetProperty(e => e.DownloadTokenExpiresAt, (DateTimeOffset?)null)
                .SetProperty(e => e.DownloadCount, e => e.DownloadCount + 1)
                .SetProperty(e => e.LastDownloadedAt, now), ct) > 0;

    public Task<PersonalDataExportArchive?> GetArchiveAsync(Guid id, CancellationToken ct = default)
        => db.PersonalDataExportArchives.AsNoTracking().FirstOrDefaultAsync(a => a.ExportId == id, ct);

    public async Task<IReadOnlyList<PersonalDataExport>> FindDueForExpiryAsync(DateTimeOffset now, int limit, CancellationToken ct = default)
        => await db.PersonalDataExports.AsNoTracking().Where(e => e.Status == PersonalDataExportStatus.Ready && e.ExpiresAt <= now)
            .OrderBy(e => e.ExpiresAt).Take(limit).ToListAsync(ct);

    public async Task<bool> ExpireAsync(Guid id, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var updated = await db.PersonalDataExports.Where(e => e.Id == id && e.Status == PersonalDataExportStatus.Ready)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, PersonalDataExportStatus.Expired)
                .SetProperty(e => e.ExpiredAt, now)
                .SetProperty(e => e.DownloadTokenHash, (string?)null)
                .SetProperty(e => e.DownloadTokenExpiresAt, (DateTimeOffset?)null), ct);
        if (updated == 0)
        {
            return false;
        }

        await db.PersonalDataExportArchives.Where(a => a.ExportId == id).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public Task<int> PurgeHistoryAsync(DateTimeOffset before, CancellationToken ct = default)
        => db.PersonalDataExports
            .Where(e => e.RequestedAt < before && e.Status != PersonalDataExportStatus.Pending && e.Status != PersonalDataExportStatus.Running
                && e.Status != PersonalDataExportStatus.Ready)
            .ExecuteDeleteAsync(ct);
}
