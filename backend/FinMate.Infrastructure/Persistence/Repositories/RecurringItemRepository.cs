using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class RecurringItemRepository(FinMateDbContext context) : IRecurringItemRepository
{
    public Task<List<RecurringItem>> GetListByUserAsync(Guid userId, CancellationToken ct = default)
        => context.RecurringItems.Where(r => r.UserId == userId).OrderBy(r => r.Kind).ThenBy(r => r.DayOfMonth).ThenBy(r => r.Name).ToListAsync(ct);

    public Task<int> CountByUserAsync(Guid userId, CancellationToken ct = default)
        => context.RecurringItems.CountAsync(r => r.UserId == userId, ct);

    public async Task AddAsync(RecurringItem item, CancellationToken ct = default)
    {
        context.RecurringItems.Add(item);
        await context.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct = default)
        => await context.RecurringItems.Where(r => r.Id == id && r.UserId == userId).ExecuteDeleteAsync(ct) > 0;
}
