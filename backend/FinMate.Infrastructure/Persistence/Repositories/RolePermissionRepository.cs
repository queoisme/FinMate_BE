using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class RolePermissionRepository : IRolePermissionRepository
{
    private readonly FinMateDbContext _context;

    public RolePermissionRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<RolePermission?> GetAsync(UserRole role, CancellationToken ct = default)
        => _context.RolePermissions.AsNoTracking().FirstOrDefaultAsync(r => r.Role == role, ct);

    public Task<List<RolePermission>> ListAsync(CancellationToken ct = default)
        => _context.RolePermissions.AsNoTracking().ToListAsync(ct);

    public async Task SaveAsync(UserRole role, long capabilities, Guid? updatedByUserId, CancellationToken ct = default)
    {
        var row = await _context.RolePermissions.FirstOrDefaultAsync(r => r.Role == role, ct);
        if (row is null)
        {
            row = new RolePermission { Role = role };
            _context.RolePermissions.Add(row);
        }

        row.Capabilities = capabilities;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedByUserId = updatedByUserId;
        await _context.SaveChangesAsync(ct);
    }
}
