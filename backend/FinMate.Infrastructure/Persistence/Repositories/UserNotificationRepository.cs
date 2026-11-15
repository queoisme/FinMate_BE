using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class UserNotificationRepository : IUserNotificationRepository
{
    private readonly FinMateDbContext _context;

    public UserNotificationRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(UserNotification notification, CancellationToken ct = default)
    {
        _context.UserNotifications.Add(notification);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<UserNotification>> ListForUserAsync(Guid userId, CancellationToken ct = default)
        => await _context.UserNotifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(ct);

    public Task<UserNotification?> GetAsync(Guid id, Guid userId, CancellationToken ct = default)
        => _context.UserNotifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct);

    public async Task UpdateAsync(UserNotification notification, CancellationToken ct = default)
    {
        _context.UserNotifications.Update(notification);
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(UserNotification notification, CancellationToken ct = default)
    {
        _context.UserNotifications.Remove(notification);
        await _context.SaveChangesAsync(ct);
    }

    public Task<int> MarkAllReadAsync(Guid userId, DateTimeOffset readAt, CancellationToken ct = default)
        => _context.UserNotifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, readAt), ct);

    public Task<int> DeleteAllAsync(Guid userId, CancellationToken ct = default)
        => _context.UserNotifications.Where(n => n.UserId == userId).ExecuteDeleteAsync(ct);
}
