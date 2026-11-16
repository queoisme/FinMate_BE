using FinMate.Application.Transactions.Transfers;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class MergeSnapshotRepository : IMergeSnapshotRepository
{
    private readonly FinMateDbContext _context;

    public MergeSnapshotRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public void Add(TransactionMergeSnapshot snapshot) => _context.TransactionMergeSnapshots.Add(snapshot);

    public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        => _context.TransactionMergeSnapshots.Where(s => s.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
}
