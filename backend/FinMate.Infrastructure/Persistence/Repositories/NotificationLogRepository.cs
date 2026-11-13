using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class NotificationLogRepository : INotificationLogRepository
{
    private readonly FinMateDbContext _context;

    public NotificationLogRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<NotificationLog?> GetProcessedByContentHashAsync(
        Guid userId, string contentHash, CancellationToken ct = default)
        => _context.NotificationLogs
            .Where(n => n.UserId == userId
                && n.ContentHash == contentHash
                && n.Status != NotificationLogStatus.Ignored)
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public Task<NotificationLog?> GetRecentIgnoredByContentHashAsync(
        Guid userId, string contentHash, DateTimeOffset since, CancellationToken ct = default)
        => _context.NotificationLogs
            .Where(n => n.UserId == userId
                && n.ContentHash == contentHash
                && n.Status == NotificationLogStatus.Ignored
                && n.CreatedAt >= since)
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public Task<List<NotificationLog>> GetFailedForRetryAsync(int maxRetryCount, CancellationToken ct = default)
        => _context.NotificationLogs
            .Where(n => n.Status == NotificationLogStatus.Failed && n.RetryCount < maxRetryCount)
            .ToListAsync(ct);

    public Task<int> PurgeBodiesOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        => _context.NotificationLogs
            .Where(n => n.CreatedAt < cutoff && n.NotificationBody != null)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.NotificationBody, (string?)null), ct);

    public async Task AddAsync(NotificationLog log, CancellationToken ct = default)
    {
        _context.NotificationLogs.Add(log);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(NotificationLog log, CancellationToken ct = default)
    {
        _context.NotificationLogs.Update(log);
        await _context.SaveChangesAsync(ct);
    }

    public async Task AddAiResultAsync(AiResult result, CancellationToken ct = default)
    {
        _context.AiResults.Add(result);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, TransactionRelation>> GetRelationHintsAsync(
        Guid userId, IReadOnlyCollection<Guid> notificationLogIds, CancellationToken ct = default)
    {
        if (notificationLogIds.Count == 0)
        {
            return new Dictionary<Guid, TransactionRelation>();
        }

        var rows = await (from r in _context.AiResults.AsNoTracking()
                          join l in _context.NotificationLogs on r.NotificationLogId equals l.Id
                          where l.UserId == userId && notificationLogIds.Contains(r.NotificationLogId)
                                && (r.RelationHint == NotificationRelationHint.Refund || r.RelationHint == NotificationRelationHint.Reversal)
                          select new { r.NotificationLogId, r.RelationHint })
            .ToListAsync(ct);
        // Chỉ hoàn/đảo: gợi ý chuyển ví (Phase 83) đi đường riêng, không phải khoản chi gốc để liên kết.
        return rows.GroupBy(r => r.NotificationLogId).ToDictionary(g => g.Key, g => g.First().RelationHint.AsRefundRelation()!.Value);
    }
}
