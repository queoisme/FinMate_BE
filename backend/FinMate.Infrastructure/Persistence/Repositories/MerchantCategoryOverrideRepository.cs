using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class MerchantCategoryOverrideRepository : IMerchantCategoryOverrideRepository
{
    private readonly FinMateDbContext _context;

    public MerchantCategoryOverrideRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<MerchantCategoryOverride?> GetAsync(Guid userId, string merchantKey, CancellationToken ct = default)
        => _context.MerchantCategoryOverrides
            .Include(o => o.Category)
            .FirstOrDefaultAsync(o => o.UserId == userId && o.MerchantKey == merchantKey, ct);

    public async Task AddAsync(MerchantCategoryOverride entity, CancellationToken ct = default)
    {
        _context.MerchantCategoryOverrides.Add(entity);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(MerchantCategoryOverride entity, CancellationToken ct = default)
    {
        _context.MerchantCategoryOverrides.Update(entity);
        await _context.SaveChangesAsync(ct);
    }
}
