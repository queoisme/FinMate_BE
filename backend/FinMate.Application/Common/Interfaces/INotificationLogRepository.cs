using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public interface INotificationLogRepository
{
    Task<NotificationLog?> GetRecentByContentHashAsync(
        Guid userId, string contentHash, DateTimeOffset since, CancellationToken ct = default);

    Task<List<NotificationLog>> GetFailedForRetryAsync(int maxRetryCount, CancellationToken ct = default);

    Task AddAsync(NotificationLog log, CancellationToken ct = default);
    Task UpdateAsync(NotificationLog log, CancellationToken ct = default);
    Task AddAiResultAsync(AiResult result, CancellationToken ct = default);
}
