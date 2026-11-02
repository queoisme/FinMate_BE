using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class AnomalyRepository : IAnomalyRepository
{
    private readonly FinMateDbContext _context;

    public AnomalyRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<TransactionAnomaly>> GetForTransactionsAsync(
        Guid userId, IReadOnlyCollection<Guid> transactionIds, CancellationToken ct = default)
    {
        if (transactionIds.Count == 0)
        {
            return [];
        }

        return await _context.TransactionAnomalies.AsNoTracking()
            .Where(a => a.UserId == userId && transactionIds.Contains(a.TransactionId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlySet<Guid>> GetNotNormalTransactionIdsAsync(Guid userId, CancellationToken ct = default)
    {
        var ids = await _context.TransactionAnomalies.AsNoTracking()
            .Where(a => a.UserId == userId && (a.Resolution == null || a.Resolution != AnomalyResolution.Normal))
            .Select(a => a.TransactionId)
            .ToListAsync(ct);
        return ids.ToHashSet();
    }

    public async Task<IReadOnlyList<PriorNormal>> GetPriorNormalsAsync(Guid userId, CancellationToken ct = default)
        => await _context.TransactionAnomalies.AsNoTracking()
            .Where(a => a.UserId == userId && a.Resolution == AnomalyResolution.Normal)
            .Select(a => new PriorNormal(a.CategoryId, a.CounterpartyKey, a.AmountCents))
            .ToListAsync(ct);

    public async Task<bool> TryAddAsync(TransactionAnomaly anomaly, CancellationToken ct = default)
    {
        _context.TransactionAnomalies.Add(anomaly);
        try
        {
            await _context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Lượt quét khác vừa đánh dấu cùng giao dịch: đã có, không phải lỗi.
            _context.Entry(anomaly).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<IReadOnlyList<TransactionAnomaly>> ListAsync(Guid userId, bool openOnly, int limit, CancellationToken ct = default)
        => await _context.TransactionAnomalies.AsNoTracking()
            .Include(a => a.Transaction)
            .Where(a => a.UserId == userId && (!openOnly || a.Resolution == null))
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public Task<TransactionAnomaly?> GetAsync(Guid id, Guid userId, CancellationToken ct = default)
        => _context.TransactionAnomalies
            .Include(a => a.Transaction)
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);

    public async Task UpdateAsync(TransactionAnomaly anomaly, CancellationToken ct = default)
    {
        // Bản lấy từ GetAsync đã được theo dõi; Update() trên cả đồ thị sẽ đánh dấu luôn giao dịch kèm theo là đã sửa.
        if (_context.Entry(anomaly).State == EntityState.Detached)
        {
            _context.TransactionAnomalies.Attach(anomaly).State = EntityState.Modified;
        }

        await _context.SaveChangesAsync(ct);
    }
}
