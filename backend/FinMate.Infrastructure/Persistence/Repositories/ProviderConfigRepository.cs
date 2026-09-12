using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class ProviderConfigRepository : IProviderConfigRepository
{
    private readonly FinMateDbContext _context;

    public ProviderConfigRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<ProviderConfig?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _context.ProviderConfigs.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<List<ProviderConfig>> GetListAsync(bool includeInactive, CancellationToken ct = default)
        => _context.ProviderConfigs
            .Where(p => includeInactive || p.IsActive)
            .OrderBy(p => p.DisplayName)
            .ToListAsync(ct);

    public Task<bool> ExistsByProviderKeyAsync(string providerKey, CancellationToken ct = default)
        => _context.ProviderConfigs.AnyAsync(p => p.ProviderKey == providerKey, ct);

    public Task<bool> ExistsByPackageNameAsync(
        string packageName, Guid? excludeId, CancellationToken ct = default)
        => _context.ProviderConfigs.AnyAsync(
            p => p.PackageName == packageName && (excludeId == null || p.Id != excludeId), ct);

    public async Task AddAsync(ProviderConfig config, CancellationToken ct = default)
    {
        _context.ProviderConfigs.Add(config);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ProviderConfig config, CancellationToken ct = default)
    {
        _context.ProviderConfigs.Update(config);
        await _context.SaveChangesAsync(ct);
    }
}
