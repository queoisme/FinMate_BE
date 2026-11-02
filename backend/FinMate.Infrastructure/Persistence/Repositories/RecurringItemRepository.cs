using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class RecurringItemRepository : IRecurringItemRepository
{
    private readonly FinMateDbContext _context;

    public RecurringItemRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<RecurringItem?> GetAsync(Guid id, Guid userId, CancellationToken ct = default)
        => _context.RecurringItems.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);

    public async Task<IReadOnlyList<RecurringItem>> ListAsync(Guid userId, RecurringStatus? status, CancellationToken ct = default)
    {
        var query = _context.RecurringItems.AsNoTracking().Where(r => r.UserId == userId);
        query = status is { } wanted
            ? query.Where(r => r.Status == wanted)
            : query.Where(r => r.Status != RecurringStatus.Cancelled && r.Status != RecurringStatus.Rejected);
        return await query.ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RecurringItem>> ListAllTrackedAsync(Guid userId, CancellationToken ct = default)
        => await _context.RecurringItems.Where(r => r.UserId == userId).ToListAsync(ct);

    public Task<RecurringItem?> FindByCounterpartyAsync(Guid userId, RecurringDirection direction, string counterpartyKey, CancellationToken ct = default)
        => _context.RecurringItems.FirstOrDefaultAsync(
            r => r.UserId == userId && r.Direction == direction && r.CounterpartyKey == counterpartyKey, ct);

    public async Task AddAsync(RecurringItem item, CancellationToken ct = default)
    {
        _context.RecurringItems.Add(item);
        await _context.SaveChangesAsync(ct);
    }

    public Task AddRangeAsync(IEnumerable<RecurringItem> items, CancellationToken ct = default)
    {
        _context.RecurringItems.AddRange(items);
        return Task.CompletedTask;
    }

    public async Task UpdateAsync(RecurringItem item, CancellationToken ct = default)
    {
        _context.RecurringItems.Update(item);
        await _context.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
}
