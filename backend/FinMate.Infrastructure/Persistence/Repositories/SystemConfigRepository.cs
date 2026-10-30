using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class SystemConfigRepository : ISystemConfigRepository
{
    private readonly FinMateDbContext _context;

    public SystemConfigRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<SystemConfigVersion?> GetActiveAsync(string name, CancellationToken ct = default)
        => _context.SystemConfigVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Name == name && v.Status == SystemConfigStatus.Active, ct);

    public Task<SystemConfigVersion?> GetVersionAsync(string name, int version, CancellationToken ct = default)
        => _context.SystemConfigVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Name == name && v.Version == version, ct);

    public async Task<(List<SystemConfigVersion> Items, int Total)> ListAsync(string name, int limit, int offset, CancellationToken ct = default)
    {
        var query = _context.SystemConfigVersions.AsNoTracking().Where(v => v.Name == name);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(v => v.Version).Skip(offset).Take(limit).ToListAsync(ct);
        return (items, total);
    }

    public async Task<bool> TryActivateNextAsync(SystemConfigVersion next, int expectedVersion, CancellationToken ct = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        var active = await _context.SystemConfigVersions
            .FirstOrDefaultAsync(v => v.Name == next.Name && v.Status == SystemConfigStatus.Active, ct);
        var activeVersion = active?.Version ?? 0;
        if (activeVersion != expectedVersion)
        {
            return false;
        }

        try
        {
            if (active is not null)
            {
                // Lưu trữ bản cũ TRƯỚC khi thêm bản mới: chỉ mục duy nhất "một bản đang chạy" không cho hai dòng active cùng tồn tại dù chỉ trong giao dịch.
                active.Status = SystemConfigStatus.Archived;
                await _context.SaveChangesAsync(ct);
            }

            next.Version = activeVersion + 1;
            _context.SystemConfigVersions.Add(next);
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Hai người lưu cùng lúc: giao dịch của người thua phạm chỉ mục duy nhất; coi như bản họ nhìn thấy đã cũ.
            _context.ChangeTracker.Clear();
            return false;
        }
    }
}
