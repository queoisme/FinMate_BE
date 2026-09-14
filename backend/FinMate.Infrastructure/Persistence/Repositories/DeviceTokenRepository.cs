using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class DeviceTokenRepository : IDeviceTokenRepository
{
    private readonly FinMateDbContext _context;

    public DeviceTokenRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<DeviceToken>> GetForUserAsync(Guid userId, CancellationToken ct = default)
        => await _context.DeviceTokens
            .Where(d => d.UserId == userId)
            .ToListAsync(ct);

    public Task<DeviceToken?> GetByTokenAsync(string token, CancellationToken ct = default)
        => _context.DeviceTokens.FirstOrDefaultAsync(d => d.Token == token, ct);

    public async Task AddAsync(DeviceToken deviceToken, CancellationToken ct = default)
    {
        _context.DeviceTokens.Add(deviceToken);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(DeviceToken deviceToken, CancellationToken ct = default)
    {
        _context.DeviceTokens.Update(deviceToken);
        await _context.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid userId, string token, CancellationToken ct = default)
        => await _context.DeviceTokens
            .Where(d => d.UserId == userId && d.Token == token)
            .ExecuteDeleteAsync(ct);

    public async Task RemoveManyAsync(IReadOnlyList<string> tokens, CancellationToken ct = default)
    {
        if (tokens.Count == 0)
        {
            return;
        }

        await _context.DeviceTokens
            .Where(d => tokens.Contains(d.Token))
            .ExecuteDeleteAsync(ct);
    }
}
