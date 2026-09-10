using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class FinancialAccountRepository : IFinancialAccountRepository
{
    private readonly FinMateDbContext _context;

    public FinancialAccountRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<FinancialAccount?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default)
        => _context.FinancialAccounts
            .Include(a => a.ProviderConfig)
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);

    public Task<List<FinancialAccount>> GetListByUserAsync(Guid userId, CancellationToken ct = default)
        => _context.FinancialAccounts
            .Include(a => a.ProviderConfig)
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

    public Task<bool> ExistsByUserAndPackageNameAsync(Guid userId, string packageName, CancellationToken ct = default)
        => _context.FinancialAccounts
            .AnyAsync(a => a.UserId == userId && a.PackageName == packageName, ct);

    public async Task AddAsync(FinancialAccount account, CancellationToken ct = default)
    {
        _context.FinancialAccounts.Add(account);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(FinancialAccount account, CancellationToken ct = default)
    {
        _context.FinancialAccounts.Update(account);
        await _context.SaveChangesAsync(ct);
    }
}
