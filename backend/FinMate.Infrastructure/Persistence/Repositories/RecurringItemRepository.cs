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

    public async Task<RecurringItem?> GetAsync(Guid id, Guid userId, CancellationToken ct = default)
        => await WithPaidAsync(await _context.RecurringItems.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct), userId, ct);

    public async Task<IReadOnlyList<RecurringItem>> ListAsync(Guid userId, RecurringStatus? status, CancellationToken ct = default)
    {
        var query = _context.RecurringItems.AsNoTracking().Where(r => r.UserId == userId);
        query = status is { } wanted
            ? query.Where(r => r.Status == wanted)
            : query.Where(r => r.Status != RecurringStatus.Cancelled && r.Status != RecurringStatus.Rejected && r.Status != RecurringStatus.Completed);
        return await WithPaidAsync(await query.ToListAsync(ct), userId, ct);
    }

    public async Task<IReadOnlyList<RecurringItem>> ListAllTrackedAsync(Guid userId, CancellationToken ct = default)
        => await WithPaidAsync(await _context.RecurringItems.Where(r => r.UserId == userId).ToListAsync(ct), userId, ct);

    public async Task<RecurringItem?> FindByCounterpartyAsync(Guid userId, RecurringDirection direction, string counterpartyKey, CancellationToken ct = default)
        => await WithPaidAsync(
            await _context.RecurringItems.FirstOrDefaultAsync(
                r => r.UserId == userId && r.Direction == direction && r.CounterpartyKey == counterpartyKey && r.Status != RecurringStatus.Completed, ct),
            userId, ct);

    public async Task<IReadOnlyDictionary<Guid, int>> CountLivePaymentsAsync(Guid userId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default)
    {
        if (itemIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        // Dòng còn sống, và giao dịch của nó (nếu có) chưa bị xoá: lưới an toàn khi một đường xoá giao dịch quên gỡ liên kết (BR-INS-03).
        return await _context.RecurringItemPayments
            .Where(p => p.UserId == userId && itemIds.Contains(p.RecurringItemId)
                        && (p.TransactionId == null || _context.Transactions.Any(t => t.Id == p.TransactionId)))
            .GroupBy(p => p.RecurringItemId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }

    public async Task<IReadOnlySet<Guid>> LinkedTransactionIdsAsync(Guid userId, CancellationToken ct = default)
        => (await _context.RecurringItemPayments
                .Where(p => p.UserId == userId && p.TransactionId != null)
                .Select(p => p.TransactionId!.Value)
                .ToListAsync(ct))
            .ToHashSet();

    public void AddPayment(RecurringItemPayment payment) => _context.RecurringItemPayments.Add(payment);

    public Task<RecurringItemPayment?> GetPaymentAsync(Guid userId, Guid itemId, Guid paymentId, CancellationToken ct = default)
        => _context.RecurringItemPayments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == paymentId && p.RecurringItemId == itemId && p.UserId == userId, ct);

    public async Task<IReadOnlyList<RecurringItemPayment>> ListPaymentsAsync(Guid userId, Guid itemId, CancellationToken ct = default)
        => await _context.RecurringItemPayments.AsNoTracking()
            .Where(p => p.UserId == userId && p.RecurringItemId == itemId)
            .OrderByDescending(p => p.DueDate).ThenByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

    public Task<RecurringItemPayment?> FindLivePaymentByTransactionAsync(Guid userId, Guid transactionId, CancellationToken ct = default)
        => _context.RecurringItemPayments.FirstOrDefaultAsync(p => p.UserId == userId && p.TransactionId == transactionId, ct);

    public async Task<T> RunWithUserLockedAsync<T>(Guid userId, Func<Task<T>> work, CancellationToken ct = default)
    {
        var owns = _context.Database.CurrentTransaction is null;
        await using var transaction = owns ? await _context.Database.BeginTransactionAsync(ct) : null;
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({LockPrefix + userId.ToString()}, 0))", ct);
        var result = await work();
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }

        return result;
    }

    // Tiền tố riêng để không trùng khoá tư vấn theo user của gói đăng ký (cùng hàm băm trên user id).
    private const string LockPrefix = "recurring:";

    private async Task<RecurringItem?> WithPaidAsync(RecurringItem? item, Guid userId, CancellationToken ct)
    {
        if (item is not null)
        {
            await WithPaidAsync([item], userId, ct);
        }

        return item;
    }

    private async Task<IReadOnlyList<RecurringItem>> WithPaidAsync(List<RecurringItem> items, Guid userId, CancellationToken ct)
    {
        var counts = await CountLivePaymentsAsync(userId, items.Select(i => i.Id).ToList(), ct);
        foreach (var item in items)
        {
            item.LivePaymentCount = counts.GetValueOrDefault(item.Id);
        }

        return items;
    }

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
