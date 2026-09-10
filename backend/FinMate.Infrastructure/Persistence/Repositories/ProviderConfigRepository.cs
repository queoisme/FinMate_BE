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
}
