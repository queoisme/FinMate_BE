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
}
