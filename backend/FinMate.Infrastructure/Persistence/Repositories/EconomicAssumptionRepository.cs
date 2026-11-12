using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class EconomicAssumptionRepository(FinMateDbContext context) : IEconomicAssumptionRepository
{
    public Task<EconomicAssumption?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => context.EconomicAssumptions.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<EconomicAssumption?> GetApprovedAsync(string key, CancellationToken ct = default)
        => context.EconomicAssumptions
            .Where(a => a.Key == key && a.Status == AssumptionStatus.Approved)
            .OrderByDescending(a => a.Version)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<string>> ListApprovedKeysAsync(CancellationToken ct = default)
        => await context.EconomicAssumptions
            .Where(a => a.Status == AssumptionStatus.Approved)
            .Select(a => a.Key)
            .Distinct()
            .OrderBy(k => k)
            .ToListAsync(ct);

    public async Task<int> GetMaxVersionAsync(string key, CancellationToken ct = default)
        => await context.EconomicAssumptions.Where(a => a.Key == key).MaxAsync(a => (int?)a.Version, ct) ?? 0;

    public Task<List<EconomicAssumption>> ListAsync(string? key, CancellationToken ct = default)
        => context.EconomicAssumptions
            .Where(a => key == null || a.Key == key)
            .OrderBy(a => a.Key)
            .ThenByDescending(a => a.Version)
            .ToListAsync(ct);

    public async Task AddAsync(EconomicAssumption assumption, CancellationToken ct = default)
    {
        context.EconomicAssumptions.Add(assumption);
        await context.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
