using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class FinanceSnapshotRepository : IFinanceSnapshotRepository
{
    private readonly FinMateDbContext _context;

    public FinanceSnapshotRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public async Task<long> GetDebitTotalAsync(Guid userId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
        => await _context.Transactions
            .Where(t => t.UserId == userId
                && t.Status == TransactionStatus.Confirmed
                && t.TransactionType == TransactionType.Debit   // loại cả chuyển khoản nội bộ (ARCHITECTURE.md §4.3)
                && t.TransactedAt >= start
                && t.TransactedAt < end)
            .SumAsync(t => (long?)t.AmountCents, ct) ?? 0;

    public Task<Dictionary<Guid, long>> GetGoalDepositsAsync(Guid userId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
        => _context.GoalContributions
            .Where(c => c.UserId == userId && c.ContributedAt >= start && c.ContributedAt < end)
            .GroupBy(c => c.SavingGoalId)
            .Select(g => new { GoalId = g.Key, Total = g.Sum(c => c.AmountCents) })
            .ToDictionaryAsync(x => x.GoalId, x => x.Total, ct);
}
