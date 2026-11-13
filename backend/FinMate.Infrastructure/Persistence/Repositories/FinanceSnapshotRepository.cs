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
                // Chi ròng (Phase 81): chi chưa bị đảo trừ hoàn; loại chuyển khoản nội bộ (ARCHITECTURE.md §4.3). Cho vay/mượn giữ nguyên như trước.
                && ((t.TransactionType == TransactionType.Debit && !t.IsReversed)
                    || (t.TransactionType == TransactionType.Credit && t.Relation == TransactionRelation.Refund))
                && t.TransactedAt >= start
                && t.TransactedAt < end)
            .SumAsync(t => (long?)(t.TransactionType == TransactionType.Debit ? t.AmountCents : -t.AmountCents), ct) is { } net && net > 0 ? net : 0;

    public Task<Dictionary<Guid, long>> GetGoalDepositsAsync(Guid userId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
        => _context.GoalContributions
            .Where(c => c.UserId == userId && c.SavingGoal!.GroupId == null && c.ContributedAt >= start && c.ContributedAt < end)
            .GroupBy(c => c.SavingGoalId)
            .Select(g => new { GoalId = g.Key, Total = g.Sum(c => c.AmountCents) })
            .ToDictionaryAsync(x => x.GoalId, x => x.Total, ct);
}
