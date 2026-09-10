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

    public Task<NotificationLog?> GetRecentByContentHashAsync(
        Guid userId, string contentHash, DateTimeOffset since, CancellationToken ct = default)
        => _context.NotificationLogs
            .Where(n => n.UserId == userId && n.ContentHash == contentHash && n.CreatedAt >= since)
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public Task<List<NotificationLog>> GetFailedForRetryAsync(int maxRetryCount, CancellationToken ct = default)
        => _context.NotificationLogs
            .Where(n => n.Status == NotificationLogStatus.Failed && n.RetryCount < maxRetryCount)
            .ToListAsync(ct);

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
}
